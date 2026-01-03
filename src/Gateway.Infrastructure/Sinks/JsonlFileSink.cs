using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;
using Gateway.Core.Models;
using Gateway.Core.Pipeline;
using Microsoft.Extensions.Logging;

namespace Gateway.Infrastructure.Sinks;

/// <summary>
/// JSONL file sink for debugging
/// </summary>
public sealed class JsonlFileSink : ISink, IAsyncDisposable
{
    private readonly string _filePath;
    private readonly ILogger<JsonlFileSink> _logger;
    private readonly Channel<TelemetryEvent> _inputChannel;
    private readonly JsonSerializerOptions _jsonOptions;
    private Task? _processingTask;
    private CancellationTokenSource? _cancellationTokenSource;
    private StreamWriter? _streamWriter;
    private readonly SemaphoreSlim _writeSemaphore = new(1, 1);

    public JsonlFileSink(
        string filePath,
        ILogger<JsonlFileSink> logger,
        BoundedChannelOptions? channelOptions = null)
    {
        _filePath = filePath;
        _logger = logger;
        
        var options = channelOptions ?? new BoundedChannelOptions(1000)
        {
            FullMode = BoundedChannelFullMode.Wait
        };
        
        _inputChannel = Channel.CreateBounded<TelemetryEvent>(options);
        
        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = false
        };
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
            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            _streamWriter = new StreamWriter(_filePath, append: true);
            
            _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _processingTask = ProcessAsync(_cancellationTokenSource.Token);
            
            _logger.LogInformation("JSONL File Sink started: {FilePath}", _filePath);
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

        _cancellationTokenSource?.Dispose();
        
        await _writeSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _streamWriter?.Dispose();
            _streamWriter = null;
        }
        finally
        {
            _writeSemaphore.Release();
        }

        _processingTask = null;
        
        _logger.LogInformation("JSONL File Sink stopped");
    }

    private async Task ProcessAsync(CancellationToken cancellationToken)
    {
        await foreach (var telemetryEvent in _inputChannel.Reader.ReadAllAsync(cancellationToken))
        {
            try
            {
                await WriteLineAsync(telemetryEvent, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error writing telemetry event {EventId} to file", telemetryEvent.Id);
            }
        }
    }

    private async Task WriteLineAsync(TelemetryEvent telemetryEvent, CancellationToken cancellationToken)
    {
        await _writeSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_streamWriter == null)
            {
                return;
            }

            var json = JsonSerializer.Serialize(telemetryEvent, _jsonOptions);
            await _streamWriter.WriteLineAsync(json).ConfigureAwait(false);
            await _streamWriter.FlushAsync().ConfigureAwait(false);
        }
        finally
        {
            _writeSemaphore.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _writeSemaphore.Dispose();
    }
}

