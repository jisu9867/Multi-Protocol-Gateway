using System.Threading.Channels;
using Gateway.Core.Models;
using Gateway.Core.Pipeline;
using Gateway.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Gateway.Infrastructure.Sinks;

/// <summary>
/// PostgreSQL sink implementation
/// </summary>
public sealed class PostgreSqlSink : ISink
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PostgreSqlSink> _logger;
    private readonly Channel<TelemetryEvent> _inputChannel;
    private Task? _processingTask;
    private CancellationTokenSource? _cancellationTokenSource;

    public PostgreSqlSink(
        IServiceScopeFactory scopeFactory,
        ILogger<PostgreSqlSink> logger,
        BoundedChannelOptions? channelOptions = null)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        
        var options = channelOptions ?? new BoundedChannelOptions(1000)
        {
            FullMode = BoundedChannelFullMode.Wait
        };
        
        _inputChannel = Channel.CreateBounded<TelemetryEvent>(options);
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
        
        _logger.LogInformation("PostgreSQL Sink started");
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
        _processingTask = null;
        
        _logger.LogInformation("PostgreSQL Sink stopped");
    }

    private async Task ProcessAsync(CancellationToken cancellationToken)
    {
        await foreach (var telemetryEvent in _inputChannel.Reader.ReadAllAsync(cancellationToken))
        {
            try
            {
                await PersistAsync(telemetryEvent, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error persisting telemetry event {EventId}", telemetryEvent.Id);
            }
        }
    }

    private async Task PersistAsync(TelemetryEvent telemetryEvent, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        await using var dbContext = scope.ServiceProvider.GetRequiredService<GatewayDbContext>();

        var entity = new TelemetryEventEntity
        {
            Id = telemetryEvent.Id,
            SourceId = telemetryEvent.SourceId,
            AdapterId = telemetryEvent.AdapterId,
            Timestamp = telemetryEvent.Timestamp,
            Payload = telemetryEvent.Payload,
            Metadata = telemetryEvent.Metadata
        };

        dbContext.TelemetryEvents.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}

