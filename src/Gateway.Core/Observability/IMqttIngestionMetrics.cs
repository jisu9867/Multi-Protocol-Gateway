namespace Gateway.Core.Observability;

/// <summary>
/// Port for recording MQTT ingestion metrics.
/// </summary>
public interface IMqttIngestionMetrics
{
    void RecordMessageIngested(string topic, TimeSpan latency);
    void RecordIngestError(string topic);
}

/// <summary>
/// No-op default implementation for cases where observability is not configured.
/// </summary>
public sealed class NullMqttIngestionMetrics : IMqttIngestionMetrics
{
    public static readonly NullMqttIngestionMetrics Instance = new();

    private NullMqttIngestionMetrics()
    {
    }

    public void RecordMessageIngested(string topic, TimeSpan latency)
    {
    }

    public void RecordIngestError(string topic)
    {
    }
}
