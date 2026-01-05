using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;
using Gateway.Core.Models;
using Gateway.Core.Pipeline;
using Gateway.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Gateway.Infrastructure.Sinks;

/// <summary>
/// JSONL file sink implementation with date-based file naming and batch flushing
/// </summary>
public sealed class JsonlFileSink : ISink, IAsyncDisposable
{
    private readonly ILogger<JsonlFileSink> _logger;
    private readonly Channel<TelemetryEvent> _inputChannel;
    private readonly JsonlFileSinkOptions _options;
    private Task? _processingTask;
    private CancellationTokenSource? _cancellationTokenSource;
    private readonly ConcurrentBag<TelemetryEvent> _buffer = new();
    private readonly SemaphoreSlim _bufferLock = new(1, 1);
    private readonly Timer? _flushTimer;
    private DateTime _currentDate = DateTime.UtcNow.Date;
    private StreamWriter? _currentWriter;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions;

    public JsonlFileSink(
        ILogger<JsonlFileSink> logger,
        IOptions<SinkOptions> sinkOptions,
        BoundedChannelOptions? channelOptions = null)
    {
        _logger = logger;
        _options = sinkOptions.Value.JsonlFile;
        
        var options = channelOptions ?? new BoundedChannelOptions(1000)
        {
            FullMode = BoundedChannelFullMode.Wait
        };
        
        _inputChannel = Channel.CreateBounded<TelemetryEvent>(options);

        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = false,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        // Setup flush timer
        if (_options.FlushIntervalMs > 0)
        {
            _flushTimer = new Timer(OnFlushTimer, null, Timeout.Infinite, Timeout.Infinite);
        }
    }

    public Channel<TelemetryEvent> InputChannel => _inputChannel;

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_processingTask != null)
        {
            return Task.CompletedTask;
        }

        try
        {
            // Ensure base directory exists
            if (!Directory.Exists(_options.BasePath))
            {
                Directory.CreateDirectory(_options.BasePath);
            }

            // Initialize current writer
            _ = Task.Run(async () =>
            {
                try
                {
                    await EnsureWriterAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error initializing file writer");
                }
            }, cancellationToken);
            
            _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _processingTask = ProcessAsync(_cancellationTokenSource.Token);
            
            // Start flush timer
            if (_flushTimer != null)
            {
                _flushTimer.Change(_options.FlushIntervalMs, _options.FlushIntervalMs);
            }
            
            _logger.LogInformation("JSONL File Sink started (BasePath: {BasePath}, BatchSize: {BatchSize}, FlushInterval: {FlushInterval}ms)",
                _options.BasePath, _options.BatchSize, _options.FlushIntervalMs);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting JSONL File Sink");
            throw;
        }

        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_processingTask == null)
        {
            return;
        }

        // Stop flush timer
        _flushTimer?.Change(Timeout.Infinite, Timeout.Infinite);

        _inputChannel.Writer.Complete();
        
        if (_cancellationTokenSource != null)
        {
            _cancellationTokenSource.Cancel();
        }

        try
        {
            await _processingTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected
        }

        // Flush remaining buffer
        await FlushBufferAsync(cancellationToken).ConfigureAwait(false);

        // Close current writer
        await CloseWriterAsync().ConfigureAwait(false);

        _cancellationTokenSource?.Dispose();
        _processingTask = null;
        
        _logger.LogInformation("JSONL File Sink stopped");
    }

    private async Task ProcessAsync(CancellationToken cancellationToken)
    {
        await foreach (var telemetryEvent in _inputChannel.Reader.ReadAllAsync(cancellationToken))
        {
            try
            {
                await BufferEventAsync(telemetryEvent, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error buffering telemetry event {EventId}", telemetryEvent.EventId);
            }
        }
    }

    private async Task BufferEventAsync(TelemetryEvent telemetryEvent, CancellationToken cancellationToken)
    {
        await _bufferLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _buffer.Add(telemetryEvent);
            
            if (_buffer.Count >= _options.BatchSize)
            {
                await FlushBufferAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _bufferLock.Release();
        }
    }

    private void OnFlushTimer(object? state)
    {
        if (_buffer.IsEmpty)
        {
            return;
        }

        // Fire and forget - timer callback doesn't support async
        _ = Task.Run(async () =>
        {
            try
            {
                await FlushBufferAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error flushing buffer from timer");
            }
        });
    }

    private async Task FlushBufferAsync(CancellationToken cancellationToken)
    {
        if (_buffer.IsEmpty)
        {
            return;
        }

        List<TelemetryEvent> eventsToFlush;
        await _bufferLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_buffer.IsEmpty)
            {
                return;
            }

            eventsToFlush = new List<TelemetryEvent>(_buffer.Count);
            while (_buffer.TryTake(out var telemetryEvent))
            {
                eventsToFlush.Add(telemetryEvent);
            }
        }
        finally
        {
            _bufferLock.Release();
        }

        if (eventsToFlush.Count == 0)
        {
            return;
        }

        await WriteEventsAsync(eventsToFlush, cancellationToken).ConfigureAwait(false);
    }

    private async Task WriteEventsAsync(List<TelemetryEvent> events, CancellationToken cancellationToken)
    {
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureWriterAsync(cancellationToken).ConfigureAwait(false);

            if (_currentWriter == null)
            {
                _logger.LogWarning("Writer is null, cannot write events");
                return;
            }

            foreach (var telemetryEvent in events)
            {
                var json = JsonSerializer.Serialize(telemetryEvent, _jsonOptions);
                await _currentWriter.WriteLineAsync(json).ConfigureAwait(false);
            }

            await _currentWriter.FlushAsync().ConfigureAwait(false);
            
            _logger.LogDebug("Flushed {Count} events to JSONL file", events.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error writing {Count} events to JSONL file", events.Count);
            throw;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task EnsureWriterAsync(CancellationToken cancellationToken)
    {
        var today = DateTime.UtcNow.Date;
        
        if (_currentWriter != null && _currentDate == today)
        {
            return; // Current writer is still valid
        }

        // Close previous writer if date changed
        if (_currentWriter != null && _currentDate != today)
        {
            await CloseWriterAsync().ConfigureAwait(false);
        }

        // Open new writer for today
        _currentDate = today;
        var fileName = $"events-{_currentDate:yyyyMMdd}.jsonl";
        var filePath = Path.Combine(_options.BasePath, fileName);

        try
        {
            _currentWriter = new StreamWriter(filePath, append: true);
            _logger.LogDebug("Opened JSONL file: {FilePath}", filePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error opening JSONL file: {FilePath}", filePath);
            throw;
        }
    }

    private async Task CloseWriterAsync()
    {
        if (_currentWriter == null)
        {
            return;
        }

        try
        {
            await _currentWriter.FlushAsync().ConfigureAwait(false);
            _currentWriter.Dispose();
            _currentWriter = null;
            _logger.LogDebug("Closed JSONL file writer");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error closing JSONL file writer");
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _flushTimer?.Dispose();
        _bufferLock.Dispose();
        _writeLock.Dispose();
        _currentWriter?.Dispose();
    }
}

