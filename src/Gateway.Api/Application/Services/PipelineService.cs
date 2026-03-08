using Gateway.Core.Adapters;
using Gateway.Core.Pipeline;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Gateway.Application.Services;

/// <summary>
/// Hosted service that orchestrates the pipeline.
/// </summary>
public sealed class PipelineService : BackgroundService
{
    private readonly ILogger<PipelineService> _logger;
    private readonly IIngest _ingest;
    private readonly INormalize _normalize;
    private readonly IRoute _route;
    private readonly IEnumerable<ISink> _sinks;
    private readonly IEnumerable<IAdapter> _adapters;
    private readonly IPipelineMetrics _metrics;
    private readonly IEventPublisher? _eventPublisher;

    public PipelineService(
        ILogger<PipelineService> logger,
        IIngest ingest,
        INormalize normalize,
        IRoute route,
        IEnumerable<ISink> sinks,
        IEnumerable<IAdapter> adapters,
        IPipelineMetrics metrics,
        IEventPublisher? eventPublisher = null)
    {
        _logger = logger;
        _ingest = ingest;
        _normalize = normalize;
        _route = route;
        _sinks = sinks;
        _adapters = adapters;
        _metrics = metrics;
        _eventPublisher = eventPublisher;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            _logger.LogDebug("Starting pipeline service");

            await _ingest.StartAsync(stoppingToken).ConfigureAwait(false);
            await _normalize.StartAsync(stoppingToken).ConfigureAwait(false);

            if (_eventPublisher != null)
            {
                await _eventPublisher.StartAsync(stoppingToken).ConfigureAwait(false);
            }
            else
            {
                _logger.LogWarning("Event publisher is null; falling back to RouteStage.");
                await _route.StartAsync(stoppingToken).ConfigureAwait(false);
            }

            foreach (var sink in _sinks)
            {
                await sink.StartAsync(stoppingToken).ConfigureAwait(false);
            }

            await ConnectPipelineAsync(stoppingToken).ConfigureAwait(false);

            foreach (var adapter in _adapters)
            {
                try
                {
                    await adapter.StartAsync(stoppingToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to start adapter {AdapterId}", adapter.Id);
                    throw;
                }
            }

            _logger.LogInformation(
                "Pipeline started: {AdapterCount} adapter(s), EventPublisher {PublisherStatus}",
                _adapters.Count(),
                _eventPublisher != null ? "on" : "off");

            await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Pipeline service error");
            throw;
        }
        finally
        {
            await ShutdownAsync().ConfigureAwait(false);
        }
    }

    private Task ConnectPipelineAsync(CancellationToken cancellationToken)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await foreach (var rawData in _ingest.InputChannel.Reader.ReadAllAsync(cancellationToken))
                {
                    try
                    {
                        await _normalize.InputChannel.Writer.WriteAsync(rawData, cancellationToken)
                            .ConfigureAwait(false);
                        _metrics.RecordIngested();
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
        }, cancellationToken);

        _ = Task.Run(async () =>
        {
            try
            {
                await foreach (var telemetryEvent in _normalize.OutputChannel.Reader.ReadAllAsync(cancellationToken))
                {
                    try
                    {
                        if (_eventPublisher != null)
                        {
                            await _eventPublisher.InputChannel.Writer.WriteAsync(telemetryEvent, cancellationToken)
                                .ConfigureAwait(false);
                            _metrics.RecordNormalized();
                        }
                        else
                        {
                            await _route.InputChannel.Writer.WriteAsync(telemetryEvent, cancellationToken)
                                .ConfigureAwait(false);
                            _metrics.RecordNormalized();
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
        }, cancellationToken);

        return Task.CompletedTask;
    }

    private async Task ShutdownAsync()
    {
        _logger.LogDebug("Shutting down pipeline service");

        foreach (var adapter in _adapters)
        {
            try
            {
                await adapter.StopAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error stopping adapter {AdapterId}", adapter.Id);
            }
        }

        if (_eventPublisher != null)
        {
            await _eventPublisher.StopAsync().ConfigureAwait(false);
        }
        else
        {
            await _route.StopAsync().ConfigureAwait(false);
        }
        await _normalize.StopAsync().ConfigureAwait(false);
        await _ingest.StopAsync().ConfigureAwait(false);

        foreach (var sink in _sinks)
        {
            try
            {
                await sink.StopAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error stopping sink");
            }
        }

        _logger.LogDebug("Pipeline service stopped");
    }
}
