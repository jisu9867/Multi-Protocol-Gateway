using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Gateway.Infrastructure.Observability;

/// <summary>
/// SignalR-specific metrics exporter using OpenTelemetry Meter
/// Tracks real-time message broadcasting metrics
/// Supports factory_id + line_id granularity for monitoring
/// </summary>
public sealed class SignalRMetrics
{
    private static readonly Meter Meter = new("Gateway.SignalR", "1.0.0");
    
    // Counter: Total messages sent (with factory_id, line_id, tag, status)
    private static readonly Counter<long> SignalRMessagesSentTotal = Meter.CreateCounter<long>(
        "signalr_messages_sent_total",
        "messages",
        "Total number of SignalR messages sent");

    // Histogram: Send latency (with factory_id, line_id, tag)
    private static readonly Histogram<double> SignalRSendLatency = Meter.CreateHistogram<double>(
        "signalr_send_latency_seconds",
        "seconds",
        "SignalR message send latency in seconds");

    // Store current client counts for ObservableGauge
    private static readonly ConcurrentDictionary<string, long> _connectedClients = new();

    static SignalRMetrics()
    {
        // Register observable gauge for connected clients
        Meter.CreateObservableGauge(
            "signalr_connected_clients",
            GetConnectedClientsMeasurements,
            "clients",
            "Number of connected SignalR clients");
    }

    private static IEnumerable<Measurement<long>> GetConnectedClientsMeasurements()
    {
        foreach (var kvp in _connectedClients)
        {
            var parts = kvp.Key.Split(':');
            if (parts.Length == 3)
            {
                var factoryId = parts[0];
                var lineId = parts[1];
                var tag = parts[2];
                
                yield return new Measurement<long>(
                    kvp.Value,
                    new TagList
                    {
                        { "factory_id", factoryId },
                        { "line_id", lineId },
                        { "tag", tag }
                    });
            }
        }
    }

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
        var tags = new TagList
        {
            { "factory_id", safeFactoryId },
            { "line_id", safeLineId },
            { "tag", safeTag },
            { "status", "success" }
        };
        try
        {
            SignalRMessagesSentTotal.Add(1, tags);
            
            var latencyTags = new TagList
            {
                { "factory_id", safeFactoryId },
                { "line_id", safeLineId },
                { "tag", safeTag }
            };
            SignalRSendLatency.Record(duration.TotalSeconds, latencyTags);
        }
        catch (Exception ex)
        {
            // Log error but don't throw - metrics recording should not break the application
            System.Diagnostics.Debug.WriteLine($"SignalRMetrics.RecordMessageSent error: {ex.Message}");
        }
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

        var tags = new TagList
        {
            { "factory_id", safeFactoryId },
            { "line_id", safeLineId },
            { "tag", safeTag },
            { "status", "error" }
        };
        SignalRMessagesSentTotal.Add(1, tags);
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

        // Store current value for ObservableGauge
        var key = $"{safeFactoryId}:{safeLineId}:{safeTag}";
        _connectedClients.AddOrUpdate(key, count, (k, v) => count);
    }

    /// <summary>
    /// Initialize SignalR metrics with dummy values to ensure they are always visible in Prometheus
    /// This should be called during application startup
    /// Note: OTel Counters are only exposed when they have been incremented, so we initialize with 1
    /// </summary>
    public static void InitializeMetrics()
    {
        var factories = new[] { "ulsan", "asan", "jeonju" };
        var lines = new[] { "line1", "line2", "line3" };
        var tags = new[] { "temp", "humidity", "pressure", "vibration", "power", "flow" };

        foreach (var factory in factories)
        {
            foreach (var line in lines)
            {
                foreach (var tag in tags)
                {
                    // Initialize counters with 1 to ensure visibility (will be incremented when real messages are sent)
                    // OTel Counters are only exposed when they have been incremented
                    var successTags = new TagList
                    {
                        { "factory_id", factory },
                        { "line_id", line },
                        { "tag", tag },
                        { "status", "success" }
                    };
                    SignalRMessagesSentTotal.Add(1, successTags);

                    var errorTags = new TagList
                    {
                        { "factory_id", factory },
                        { "line_id", line },
                        { "tag", tag },
                        { "status", "error" }
                    };
                    SignalRMessagesSentTotal.Add(1, errorTags);

                    // Initialize histogram with a very small value to ensure visibility
                    var latencyTags = new TagList
                    {
                        { "factory_id", factory },
                        { "line_id", line },
                        { "tag", tag }
                    };
                    SignalRSendLatency.Record(0.0001, latencyTags);

                    // Initialize connected clients gauge
                    UpdateConnectedClients(factory, line, tag, 0);
                }
            }
        }

        // Also initialize "unknown" combinations
        var unknownSuccessTags = new TagList
        {
            { "factory_id", "unknown" },
            { "line_id", "unknown" },
            { "tag", "unknown" },
            { "status", "success" }
        };
        SignalRMessagesSentTotal.Add(1, unknownSuccessTags);

        var unknownErrorTags = new TagList
        {
            { "factory_id", "unknown" },
            { "line_id", "unknown" },
            { "tag", "unknown" },
            { "status", "error" }
        };
        SignalRMessagesSentTotal.Add(1, unknownErrorTags);

        var unknownLatencyTags = new TagList
        {
            { "factory_id", "unknown" },
            { "line_id", "unknown" },
            { "tag", "unknown" }
        };
        SignalRSendLatency.Record(0.0001, unknownLatencyTags);
        UpdateConnectedClients("unknown", "unknown", "unknown", 0);
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
