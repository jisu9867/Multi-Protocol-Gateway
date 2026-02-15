using System.Diagnostics;
using Prometheus;

namespace Gateway.Infrastructure.Observability;

/// <summary>
/// SignalR-specific metrics exporter
/// Tracks real-time message broadcasting metrics
/// Supports factory_id + line_id granularity for monitoring
/// </summary>
public sealed class SignalRMetrics
{
    // Counter: Total messages sent (with factory_id, line_id, tag, status)
    private static readonly Counter SignalRMessagesSentTotal = Metrics.CreateCounter(
        "signalr_messages_sent_total",
        "Total number of SignalR messages sent",
        new[] { "factory_id", "line_id", "tag", "status" }); // status: success, error

    // Histogram: Send latency (with factory_id, line_id, tag)
    private static readonly Histogram SignalRSendLatency = Metrics.CreateHistogram(
        "signalr_send_latency_seconds",
        "SignalR message send latency in seconds",
        new[] { "factory_id", "line_id", "tag" },
        new HistogramConfiguration
        {
            Buckets = new[] { 0.001, 0.005, 0.01, 0.05, 0.1, 0.5, 1.0 }
        });

    // Gauge: Connected clients (with factory_id, line_id, tag)
    private static readonly Gauge SignalRConnectedClients = Metrics.CreateGauge(
        "signalr_connected_clients",
        "Number of connected SignalR clients",
        new[] { "factory_id", "line_id", "tag" });

    /// <summary>
    /// Record a successfully sent SignalR message
    /// </summary>
    /// <param name="factoryId">Factory identifier</param>
    /// <param name="lineId">Line identifier (extracted from SourceId)</param>
    /// <param name="tag">Event tag (e.g., "temp", "humidity")</param>
    /// <param name="duration">Send duration</param>
    public static void RecordMessageSent(string factoryId, string lineId, string tag, TimeSpan duration)
    {
        // Sanitize labels to prevent cardinality explosion
        var safeFactoryId = MetricLabelHelper.SanitizeLabelValue(factoryId);
        var safeLineId = MetricLabelHelper.SanitizeLabelValue(lineId);
        var safeTag = MetricLabelHelper.SanitizeLabelValue(tag);

        // Ensure line_id is not empty - if it's empty or "unknown", log a warning
        if (string.IsNullOrWhiteSpace(safeLineId) || safeLineId == "unknown")
        {
            // Log warning but still record the metric with "unknown" to maintain consistency
            System.Diagnostics.Debug.WriteLine($"SignalRMetrics: line_id is '{safeLineId}' for factoryId={safeFactoryId}, tag={safeTag}");
        }

        // Record metric with all labels including line_id
        SignalRMessagesSentTotal.WithLabels(safeFactoryId, safeLineId, safeTag, "success").Inc();
        SignalRSendLatency.WithLabels(safeFactoryId, safeLineId, safeTag).Observe(duration.TotalSeconds);
    }

    /// <summary>
    /// Record a failed SignalR message send
    /// </summary>
    /// <param name="factoryId">Factory identifier</param>
    /// <param name="lineId">Line identifier (extracted from SourceId)</param>
    /// <param name="tag">Event tag</param>
    public static void RecordSendError(string factoryId, string lineId, string tag)
    {
        // Sanitize labels to prevent cardinality explosion
        var safeFactoryId = MetricLabelHelper.SanitizeLabelValue(factoryId);
        var safeLineId = MetricLabelHelper.SanitizeLabelValue(lineId);
        var safeTag = MetricLabelHelper.SanitizeLabelValue(tag);

        SignalRMessagesSentTotal.WithLabels(safeFactoryId, safeLineId, safeTag, "error").Inc();
    }

    /// <summary>
    /// Update connected clients count
    /// </summary>
    /// <param name="factoryId">Factory identifier</param>
    /// <param name="lineId">Line identifier (extracted from SourceId)</param>
    /// <param name="tag">Event tag</param>
    /// <param name="count">Number of connected clients</param>
    public static void UpdateConnectedClients(string factoryId, string lineId, string tag, int count)
    {
        // Sanitize labels to prevent cardinality explosion
        var safeFactoryId = MetricLabelHelper.SanitizeLabelValue(factoryId);
        var safeLineId = MetricLabelHelper.SanitizeLabelValue(lineId);
        var safeTag = MetricLabelHelper.SanitizeLabelValue(tag);

        SignalRConnectedClients.WithLabels(safeFactoryId, safeLineId, safeTag).Set(count);
    }

    // Legacy methods for backward compatibility (will extract line_id as "unknown")
    [Obsolete("Use RecordMessageSent with lineId parameter instead")]
    public static void RecordMessageSent(string factoryId, string tag, TimeSpan duration)
    {
        RecordMessageSent(factoryId, "unknown", tag, duration);
    }

    [Obsolete("Use RecordSendError with lineId parameter instead")]
    public static void RecordSendError(string factoryId, string tag)
    {
        RecordSendError(factoryId, "unknown", tag);
    }

    [Obsolete("Use UpdateConnectedClients with lineId parameter instead")]
    public static void UpdateConnectedClients(string factoryId, string tag, int count)
    {
        UpdateConnectedClients(factoryId, "unknown", tag, count);
    }
}

