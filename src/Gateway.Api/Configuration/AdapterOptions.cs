namespace Gateway.Api.Configuration;

/// <summary>
/// Adapter configuration options
/// </summary>
public sealed class AdapterOptions
{
    public const string SectionName = "Adapters";

    /// <summary>
    /// Enable FakeAdapter
    /// </summary>
    public bool EnableFakeAdapter { get; set; } = true;

    /// <summary>
    /// MQTT adapter options
    /// </summary>
    public MqttAdapterOptions Mqtt { get; set; } = new();
}

/// <summary>
/// MQTT adapter options
/// </summary>
public sealed class MqttAdapterOptions
{
    /// <summary>
    /// Enable MQTT adapter
    /// </summary>
    public bool Enabled { get; set; } = false;

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
    public string Topic { get; set; } = "factory/+/telemetry";
}

