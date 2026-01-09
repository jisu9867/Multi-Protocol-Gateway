using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;
using Gateway.Core.Models;
using Gateway.Core.Pipeline;
using Gateway.Infrastructure.Configuration;
using Gateway.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Gateway.Infrastructure.Sinks;

/// <summary>
/// PostgreSQL sink implementation with batch processing and idempotent inserts
/// </summary>
public sealed class PostgreSqlSink : ISink, IAsyncDisposable
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PostgreSqlSink> _logger;
    private readonly Channel<TelemetryEvent> _inputChannel;
    private readonly PostgreSqlSinkOptions _options;
    private Task? _processingTask;
    private CancellationTokenSource? _cancellationTokenSource;
    private readonly ConcurrentBag<TelemetryEventEntity> _buffer = new();
    private readonly SemaphoreSlim _bufferLock = new(1, 1);
    private readonly Timer? _flushTimer;

    public PostgreSqlSink(
        IServiceScopeFactory scopeFactory,
        ILogger<PostgreSqlSink> logger,
        IOptions<SinkOptions> sinkOptions,
        BoundedChannelOptions? channelOptions = null)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _options = sinkOptions.Value.PostgreSql;
        
        var options = channelOptions ?? new BoundedChannelOptions(1000)
        {
            FullMode = BoundedChannelFullMode.Wait
        };
        
        _inputChannel = Channel.CreateBounded<TelemetryEvent>(options);

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

        _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _processingTask = ProcessAsync(_cancellationTokenSource.Token);
        
        // Start flush timer
        if (_flushTimer != null)
        {
            _flushTimer.Change(_options.FlushIntervalMs, _options.FlushIntervalMs);
        }
        
        _logger.LogInformation("PostgreSQL Sink started (BatchSize: {BatchSize}, FlushInterval: {FlushInterval}ms)",
            _options.BatchSize, _options.FlushIntervalMs);
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

        _cancellationTokenSource?.Dispose();
        _processingTask = null;
        
        _logger.LogInformation("PostgreSQL Sink stopped");
    }

    private async Task ProcessAsync(CancellationToken cancellationToken)
    {
        await foreach (var telemetryEvent in _inputChannel.Reader.ReadAllAsync(cancellationToken))
        {
            try
            {
                await BufferEventAsync(telemetryEvent, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected during shutdown - ignore silently
                // Note: TaskCanceledException is a subclass of OperationCanceledException
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error buffering telemetry event {EventId}", telemetryEvent.EventId);
            }
        }
    }

    private async Task BufferEventAsync(TelemetryEvent telemetryEvent, CancellationToken cancellationToken)
    {
        var entity = MapToEntity(telemetryEvent);
        
        await _bufferLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _buffer.Add(entity);
            
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

        List<TelemetryEventEntity> eventsToFlush;
        await _bufferLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_buffer.IsEmpty)
            {
                return;
            }

            eventsToFlush = new List<TelemetryEventEntity>(_buffer.Count);
            while (_buffer.TryTake(out var entity))
            {
                eventsToFlush.Add(entity);
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

        await BulkInsertWithConflictHandlingAsync(eventsToFlush, cancellationToken).ConfigureAwait(false);
    }

    private async Task BulkInsertWithConflictHandlingAsync(
        List<TelemetryEventEntity> entities,
        CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        await using var dbContext = scope.ServiceProvider.GetRequiredService<GatewayDbContext>();

        try
        {
            // Use AddRange for simplicity - EF Core will handle the insert
            // For idempotent inserts, we'll catch unique constraint violations
            // In production, you might want to use raw SQL with ON CONFLICT DO NOTHING for better performance
            dbContext.TelemetryEvents.AddRange(entities);
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            
            _logger.LogDebug("Bulk inserted {Count} events to PostgreSQL", entities.Count);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException pgEx && pgEx.SqlState == "23505")
        {
            // Unique constraint violation (duplicate EventId) - this is expected for idempotent operations
            // Try inserting one by one to handle partial duplicates
            _logger.LogDebug("Duplicate events detected in batch, inserting individually with conflict handling");
            
            dbContext.ChangeTracker.Clear();
            foreach (var entity in entities)
            {
                try
                {
                    dbContext.TelemetryEvents.Add(entity);
                    await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (DbUpdateException innerEx) when (innerEx.InnerException is PostgresException pgEx2 && pgEx2.SqlState == "23505")
                {
                    // Ignore duplicates - this is idempotent behavior
                    dbContext.ChangeTracker.Clear();
                }
                catch (Exception innerEx)
                {
                    _logger.LogWarning(innerEx, "Error inserting event {EventId}", entity.EventId);
                    dbContext.ChangeTracker.Clear();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error bulk inserting {Count} events to PostgreSQL", entities.Count);
            
            // Fallback: try individual inserts
            dbContext.ChangeTracker.Clear();
            foreach (var entity in entities)
            {
                try
                {
                    dbContext.TelemetryEvents.Add(entity);
                    await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (DbUpdateException innerEx) when (innerEx.InnerException is PostgresException pgEx && pgEx.SqlState == "23505")
                {
                    // Ignore duplicates
                    dbContext.ChangeTracker.Clear();
                }
                catch (Exception innerEx)
                {
                    _logger.LogWarning(innerEx, "Error inserting event {EventId}", entity.EventId);
                    dbContext.ChangeTracker.Clear();
                }
            }
        }
    }

    private static TelemetryEventEntity MapToEntity(TelemetryEvent telemetryEvent)
    {
        // Determine route key from tag prefix (simple routing based on tag)
        var routeKey = telemetryEvent.Tag.ToLowerInvariant();

        // Ensure timestamp is UTC - always use UtcDateTime property which guarantees UTC
        var utcTimestamp = telemetryEvent.Timestamp.UtcDateTime;
        
        // Double-check DateTimeKind is UTC (should always be true for UtcDateTime, but be safe)
        if (utcTimestamp.Kind != DateTimeKind.Utc)
        {
            utcTimestamp = DateTime.SpecifyKind(utcTimestamp, DateTimeKind.Utc);
        }

        return new TelemetryEventEntity
        {
            EventId = telemetryEvent.EventId,
            Timestamp = utcTimestamp,
            SourceId = telemetryEvent.SourceId,
            Tag = telemetryEvent.Tag,
            Sequence = telemetryEvent.Sequence,
            Quality = telemetryEvent.Quality,
            ValueJson = telemetryEvent.Value.GetRawText(),
            RouteKey = routeKey,
            TraceId = telemetryEvent.TraceId
        };
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _flushTimer?.Dispose();
        _bufferLock.Dispose();
    }
}
