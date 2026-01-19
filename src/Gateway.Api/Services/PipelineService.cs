using Gateway.Adapters.FakeAdapter;
using Gateway.Core.Adapters;
using Gateway.Core.Pipeline;
using Gateway.Api.Pipeline;
using Gateway.Infrastructure.Kafka;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Gateway.Api.Services;

/// <summary>
/// Hosted service that orchestrates the pipeline
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
    private readonly KafkaProducer? _kafkaProducer;

    public PipelineService(
        ILogger<PipelineService> logger,
        IIngest ingest,
        INormalize normalize,
        IRoute route,
        IEnumerable<ISink> sinks,
        IEnumerable<IAdapter> adapters,
        IPipelineMetrics metrics,
        KafkaProducer? kafkaProducer = null)
    {
        _logger = logger;
        _ingest = ingest;
        _normalize = normalize;
        _route = route;
        _sinks = sinks;
        _adapters = adapters;
        _metrics = metrics;
        _kafkaProducer = kafkaProducer;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            _logger.LogInformation("Starting pipeline service");

            // Start pipeline stages
            await _ingest.StartAsync(stoppingToken).ConfigureAwait(false);
            await _normalize.StartAsync(stoppingToken).ConfigureAwait(false);

            // Start Kafka Producer if available
            if (_kafkaProducer != null)
            {
                await _kafkaProducer.StartAsync(stoppingToken).ConfigureAwait(false);
            }
            else
            {
                // Fallback to RouteStage if Kafka is not configured
            await _route.StartAsync(stoppingToken).ConfigureAwait(false);
            }

            // Start sinks
            foreach (var sink in _sinks)
            {
                await sink.StartAsync(stoppingToken).ConfigureAwait(false);
            }

            // Connect pipeline stages
            await ConnectPipelineAsync(stoppingToken).ConfigureAwait(false);

            // Start adapters
            foreach (var adapter in _adapters)
            {
                await adapter.StartAsync(stoppingToken).ConfigureAwait(false);
            }

            _logger.LogInformation("Pipeline service started successfully");

            // Keep running until cancellation
            await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown
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
        // Connect ingest -> normalize
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
                        // Expected during shutdown
                        // Note: TaskCanceledException is a subclass of OperationCanceledException
                        break;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Expected during shutdown
                // Note: TaskCanceledException is a subclass of OperationCanceledException
            }
        }, cancellationToken);

        // Connect normalize -> Kafka Producer (if available) or RouteStage (fallback)
        _ = Task.Run(async () =>
        {
            try
        {
            await foreach (var telemetryEvent in _normalize.OutputChannel.Reader.ReadAllAsync(cancellationToken))
            {
                    try
            {
                if (_kafkaProducer != null)
                {
                    // Send to Kafka Producer
                    await _kafkaProducer.InputChannel.Writer.WriteAsync(telemetryEvent, cancellationToken)
                        .ConfigureAwait(false);
                    _metrics.RecordNormalized();
                }
                else
                {
                    // Fallback to RouteStage if Kafka is not configured
                await _route.InputChannel.Writer.WriteAsync(telemetryEvent, cancellationToken)
                    .ConfigureAwait(false);
                _metrics.RecordNormalized();
                }
                    }
                    catch (OperationCanceledException)
                    {
                        // Expected during shutdown
                        // Note: TaskCanceledException is a subclass of OperationCanceledException
                        break;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Expected during shutdown
                // Note: TaskCanceledException is a subclass of OperationCanceledException
            }
        }, cancellationToken);
        
        return Task.CompletedTask;
    }

    private async Task ShutdownAsync()
    {
        _logger.LogInformation("Shutting down pipeline service");

        // Stop adapters
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

        // Stop pipeline stages
        if (_kafkaProducer != null)
        {
            await _kafkaProducer.StopAsync().ConfigureAwait(false);
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

        _logger.LogInformation("Pipeline service stopped");
    }
}

