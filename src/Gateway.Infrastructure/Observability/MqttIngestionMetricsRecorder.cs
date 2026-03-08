using Gateway.Core.Observability;

namespace Gateway.Infrastructure.Observability;

/// <summary>
/// Infrastructure adapter that bridges the core metrics port to OpenTelemetry meters.
/// </summary>
public sealed class MqttIngestionMetricsRecorder : IMqttIngestionMetrics
{
    public void RecordMessageIngested(string topic, TimeSpan latency)
    {
        MqttMetrics.RecordMessageIngested(topic, latency);
    }

    public void RecordIngestError(string topic)
    {
        MqttMetrics.RecordIngestError(topic);
    }
}
