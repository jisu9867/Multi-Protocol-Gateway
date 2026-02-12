using System.Diagnostics;
using Prometheus;

namespace Gateway.Infrastructure.Observability;

/// <summary>
/// MQTT-specific metrics exporter
/// Tracks message ingestion latency from MQTT adapter
/// </summary>
public sealed class MqttMetrics
{
    private static readonly Counter MqttMessagesIngestedTotal = Metrics.CreateCounter(
        "mqtt_messages_ingested_total",
        "Total number of MQTT messages ingested",
        new[] { "topic", "status" }); // status: success, error

    private static readonly Histogram MqttIngestLatency = Metrics.CreateHistogram(
        "mqtt_ingest_latency_seconds",
        "MQTT message ingestion latency in seconds (from receive to pipeline)",
        new[] { "topic" },
        new HistogramConfiguration
        {
            Buckets = new[] { 0.001, 0.005, 0.01, 0.05, 0.1, 0.5, 1.0 }
        });

    /// <summary>
    /// Record a successfully ingested MQTT message
    /// </summary>
    public static void RecordMessageIngested(string topic, TimeSpan latency)
    {
        MqttMessagesIngestedTotal.WithLabels(topic, "success").Inc();
        MqttIngestLatency.WithLabels(topic).Observe(latency.TotalSeconds);
    }

    /// <summary>
    /// Record a failed MQTT message ingestion
    /// </summary>
    public static void RecordIngestError(string topic)
    {
        MqttMessagesIngestedTotal.WithLabels(topic, "error").Inc();
    }
}

