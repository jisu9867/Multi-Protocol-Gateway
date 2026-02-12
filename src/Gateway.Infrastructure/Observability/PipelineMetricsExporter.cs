using Gateway.Core.Pipeline;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Prometheus;

namespace Gateway.Infrastructure.Observability;

/// <summary>
/// Exports pipeline metrics to Prometheus
/// Bridges existing IPipelineMetrics to Prometheus metrics
/// </summary>
public sealed class PipelineMetricsExporter : IHostedService, IDisposable
{
    private readonly IPipelineMetrics _pipelineMetrics;
    private readonly ILogger<PipelineMetricsExporter> _logger;
    private Timer? _updateTimer;

    // Prometheus metrics
    private static readonly Counter PipelineIngestedTotal = Metrics.CreateCounter(
        "pipeline_ingested_total",
        "Total number of events ingested into the pipeline",
        new[] { "factory_id" });

    private static readonly Counter PipelineNormalizedTotal = Metrics.CreateCounter(
        "pipeline_normalized_total",
        "Total number of events normalized",
        new[] { "factory_id" });

    private static readonly Counter PipelineRoutedTotal = Metrics.CreateCounter(
        "pipeline_routed_total",
        "Total number of events routed to sinks",
        new[] { "factory_id" });

    private static readonly Counter PipelinePersistedTotal = Metrics.CreateCounter(
        "pipeline_persisted_total",
        "Total number of events persisted to database",
        new[] { "factory_id" });

    private static readonly Counter PipelineDroppedTotal = Metrics.CreateCounter(
        "pipeline_dropped_total",
        "Total number of events dropped",
        new[] { "factory_id" });

    private static readonly Histogram PipelineProcessingDuration = Metrics.CreateHistogram(
        "pipeline_processing_duration_seconds",
        "Pipeline processing duration in seconds",
        new[] { "stage" },
        new HistogramConfiguration
        {
            Buckets = new[] { 0.001, 0.005, 0.01, 0.05, 0.1, 0.5, 1.0, 5.0 }
        });

    private static readonly Gauge PipelineStageQueueLength = Metrics.CreateGauge(
        "pipeline_stage_queue_length",
        "Current queue length for each pipeline stage",
        new[] { "stage" });

    private long _lastIngestedCount = 0;
    private long _lastNormalizedCount = 0;
    private long _lastRoutedCount = 0;
    private long _lastPersistedCount = 0;
    private long _lastDroppedCount = 0;

    public PipelineMetricsExporter(
        IPipelineMetrics pipelineMetrics,
        ILogger<PipelineMetricsExporter> logger)
    {
        _pipelineMetrics = pipelineMetrics;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Update metrics every 5 seconds
        _updateTimer = new Timer(UpdateMetrics, null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _updateTimer?.Change(Timeout.Infinite, 0);
        return Task.CompletedTask;
    }

    private void UpdateMetrics(object? state)
    {
        try
        {
            var snapshot = _pipelineMetrics.GetSnapshot();

            // Calculate deltas and increment counters
            // Note: Using "unknown" as factory_id since IPipelineMetrics doesn't track it per factory
            // In production, you might want to enhance IPipelineMetrics to track factory_id
            var ingestedDelta = snapshot.IngestedCount - _lastIngestedCount;
            var normalizedDelta = snapshot.NormalizedCount - _lastNormalizedCount;
            var routedDelta = snapshot.RoutedCount - _lastRoutedCount;
            var persistedDelta = snapshot.PersistedCount - _lastPersistedCount;
            var droppedDelta = snapshot.DroppedCount - _lastDroppedCount;

            if (ingestedDelta > 0)
            {
                PipelineIngestedTotal.WithLabels("unknown").Inc(ingestedDelta);
            }
            if (normalizedDelta > 0)
            {
                PipelineNormalizedTotal.WithLabels("unknown").Inc(normalizedDelta);
            }
            if (routedDelta > 0)
            {
                PipelineRoutedTotal.WithLabels("unknown").Inc(routedDelta);
            }
            if (persistedDelta > 0)
            {
                PipelinePersistedTotal.WithLabels("unknown").Inc(persistedDelta);
            }
            if (droppedDelta > 0)
            {
                PipelineDroppedTotal.WithLabels("unknown").Inc(droppedDelta);
            }

            _lastIngestedCount = snapshot.IngestedCount;
            _lastNormalizedCount = snapshot.NormalizedCount;
            _lastRoutedCount = snapshot.RoutedCount;
            _lastPersistedCount = snapshot.PersistedCount;
            _lastDroppedCount = snapshot.DroppedCount;

            // Update queue lengths
            foreach (var kvp in snapshot.QueueLengths)
            {
                PipelineStageQueueLength.WithLabels(kvp.Key).Set(kvp.Value);
            }

            // Record average latency as histogram
            if (snapshot.AverageLatency.TotalSeconds > 0)
            {
                // Note: This records the average, not individual latencies
                // For more accurate histograms, IPipelineMetrics would need to expose individual latencies
                PipelineProcessingDuration.WithLabels("pipeline").Observe(snapshot.AverageLatency.TotalSeconds);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating pipeline metrics");
        }
    }

    public void Dispose()
    {
        _updateTimer?.Dispose();
    }
}

