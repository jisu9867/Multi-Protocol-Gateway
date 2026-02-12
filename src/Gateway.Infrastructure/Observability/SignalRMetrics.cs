using System.Diagnostics;
using Prometheus;

namespace Gateway.Infrastructure.Observability;

/// <summary>
/// SignalR-specific metrics exporter
/// Tracks real-time message broadcasting metrics
/// </summary>
public sealed class SignalRMetrics
{
    private static readonly Counter SignalRMessagesSentTotal = Metrics.CreateCounter(
        "signalr_messages_sent_total",
        "Total number of SignalR messages sent",
        new[] { "factory_id", "tag", "status" }); // status: success, error

    private static readonly Histogram SignalRSendLatency = Metrics.CreateHistogram(
        "signalr_send_latency_seconds",
        "SignalR message send latency in seconds",
        new[] { "factory_id", "tag" },
        new HistogramConfiguration
        {
            Buckets = new[] { 0.001, 0.005, 0.01, 0.05, 0.1, 0.5, 1.0 }
        });

    private static readonly Gauge SignalRConnectedClients = Metrics.CreateGauge(
        "signalr_connected_clients",
        "Number of connected SignalR clients",
        new[] { "factory_id", "tag" });

    /// <summary>
    /// Record a successfully sent SignalR message
    /// </summary>
    public static void RecordMessageSent(string factoryId, string tag, TimeSpan duration)
    {
        SignalRMessagesSentTotal.WithLabels(factoryId, tag, "success").Inc();
        SignalRSendLatency.WithLabels(factoryId, tag).Observe(duration.TotalSeconds);
    }

    /// <summary>
    /// Record a failed SignalR message send
    /// </summary>
    public static void RecordSendError(string factoryId, string tag)
    {
        SignalRMessagesSentTotal.WithLabels(factoryId, tag, "error").Inc();
    }

    /// <summary>
    /// Update connected clients count
    /// </summary>
    public static void UpdateConnectedClients(string factoryId, string tag, int count)
    {
        SignalRConnectedClients.WithLabels(factoryId, tag).Set(count);
    }
}

