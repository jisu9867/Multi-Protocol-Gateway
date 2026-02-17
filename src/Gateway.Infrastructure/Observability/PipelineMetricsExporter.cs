using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Gateway.Core.Pipeline;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Gateway.Infrastructure.Observability;

/// <summary>
/// Exports pipeline metrics to OpenTelemetry
/// Bridges existing IPipelineMetrics to OTel metrics
/// </summary>
public sealed class PipelineMetricsExporter : IHostedService, IDisposable
{
    private readonly IPipelineMetrics _pipelineMetrics;
    private readonly ILogger<PipelineMetricsExporter> _logger;
    private Timer? _updateTimer;

    private static readonly Meter Meter = new("Gateway.Pipeline", "1.0.0");

    private static readonly Counter<long> PipelineIngestedTotal = Meter.CreateCounter<long>(
        "pipeline_ingested_total",
        "events",
        "Total number of events ingested into the pipeline");

    private static readonly Counter<long> PipelineNormalizedTotal = Meter.CreateCounter<long>(
        "pipeline_normalized_total",
        "events",
        "Total number of events normalized");

    private static readonly Counter<long> PipelineRoutedTotal = Meter.CreateCounter<long>(
        "pipeline_routed_total",
        "events",
        "Total number of events routed to sinks");

    private static readonly Counter<long> PipelinePersistedTotal = Meter.CreateCounter<long>(
        "pipeline_persisted_total",
        "events",
        "Total number of events persisted to database");

    private static readonly Counter<long> PipelineDroppedTotal = Meter.CreateCounter<long>(
        "pipeline_dropped_total",
        "events",
        "Total number of events dropped");

    private static readonly Histogram<double> PipelineProcessingDuration = Meter.CreateHistogram<double>(
        "pipeline_processing_duration_seconds",
        "seconds",
        "Pipeline processing duration in seconds");

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
        
        // Register observable gauge for queue lengths
        Meter.CreateObservableGauge(
            "pipeline_stage_queue_length",
            GetQueueLengthMeasurements,
            "items",
            "Current queue length for each pipeline stage");
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
                var tags = new TagList { { "factory_id", "unknown" } };
                PipelineIngestedTotal.Add(ingestedDelta, tags);
            }
            if (normalizedDelta > 0)
            {
                var tags = new TagList { { "factory_id", "unknown" } };
                PipelineNormalizedTotal.Add(normalizedDelta, tags);
            }
            if (routedDelta > 0)
            {
                var tags = new TagList { { "factory_id", "unknown" } };
                PipelineRoutedTotal.Add(routedDelta, tags);
            }
            if (persistedDelta > 0)
            {
                var tags = new TagList { { "factory_id", "unknown" } };
                PipelinePersistedTotal.Add(persistedDelta, tags);
            }
            if (droppedDelta > 0)
            {
                var tags = new TagList { { "factory_id", "unknown" } };
                PipelineDroppedTotal.Add(droppedDelta, tags);
            }

            _lastIngestedCount = snapshot.IngestedCount;
            _lastNormalizedCount = snapshot.NormalizedCount;
            _lastRoutedCount = snapshot.RoutedCount;
            _lastPersistedCount = snapshot.PersistedCount;
            _lastDroppedCount = snapshot.DroppedCount;

            // Record average latency as histogram
            if (snapshot.AverageLatency.TotalSeconds > 0)
            {
                // Note: This records the average, not individual latencies
                // For more accurate histograms, IPipelineMetrics would need to expose individual latencies
                var tags = new TagList { { "stage", "pipeline" } };
                PipelineProcessingDuration.Record(snapshot.AverageLatency.TotalSeconds, tags);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating pipeline metrics");
        }
    }

    private IEnumerable<Measurement<long>> GetQueueLengthMeasurements()
    {
        var measurements = new List<Measurement<long>>();
        try
        {
            var snapshot = _pipelineMetrics.GetSnapshot();
            foreach (var kvp in snapshot.QueueLengths)
            {
                measurements.Add(new Measurement<long>(
                    kvp.Value,
                    new TagList { { "stage", kvp.Key } }));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting queue length measurements");
        }
        return measurements;
    }

    public void Dispose()
    {
        _updateTimer?.Dispose();
    }
}
