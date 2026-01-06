namespace Gateway.Adapters.MqttAdapter;

/// <summary>
/// Configuration options for MQTT adapter
/// </summary>
public sealed class MqttAdapterOptions
{
    /// <summary>
    /// MQTT broker server address
    /// </summary>
    public string Server { get; set; } = "localhost";

    /// <summary>
    /// MQTT broker port
    /// </summary>
    public int Port { get; set; } = 1883;

    /// <summary>
    /// MQTT client ID (auto-generated if not specified)
    /// </summary>
    public string? ClientId { get; set; }

    /// <summary>
    /// MQTT username (optional)
    /// </summary>
    public string? Username { get; set; }

    /// <summary>
    /// MQTT password (optional)
    /// </summary>
    public string? Password { get; set; }

    /// <summary>
    /// Topic pattern to subscribe to (default: factory/+/telemetry)
    /// </summary>
    public string? Topic { get; set; }
}

