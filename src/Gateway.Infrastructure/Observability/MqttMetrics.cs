using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Gateway.Infrastructure.Observability;

/// <summary>
/// MQTT-specific metrics exporter using OpenTelemetry Meter
/// Tracks message ingestion latency from MQTT adapter
/// </summary>
public sealed class MqttMetrics
{
    private static readonly Meter Meter = new("Gateway.MQTT", "1.0.0");
    
    private static readonly Counter<long> MqttMessagesIngestedTotal = Meter.CreateCounter<long>(
        "mqtt_messages_ingested_total",
        "messages",
        "Total number of MQTT messages ingested");

    private static readonly Histogram<double> MqttIngestLatency = Meter.CreateHistogram<double>(
        "mqtt_ingest_latency_seconds",
        "seconds",
        "MQTT message ingestion latency in seconds (from receive to pipeline)");

    /// <summary>
    /// Record a successfully ingested MQTT message
    /// </summary>
    public static void RecordMessageIngested(string topic, TimeSpan latency)
    {
        var tags = new TagList
        {
            { "topic", topic },
            { "status", "success" }
        };
        MqttMessagesIngestedTotal.Add(1, tags);
        
        var latencyTags = new TagList
        {
            { "topic", topic }
        };
        MqttIngestLatency.Record(latency.TotalSeconds, latencyTags);
    }

    /// <summary>
    /// Record a failed MQTT message ingestion
    /// </summary>
    public static void RecordIngestError(string topic)
    {
        var tags = new TagList
        {
            { "topic", topic },
            { "status", "error" }
        };
        MqttMessagesIngestedTotal.Add(1, tags);
    }
}
