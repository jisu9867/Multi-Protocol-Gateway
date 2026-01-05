namespace Gateway.Core.Models;

/// <summary>
/// Device connection status information
/// </summary>
public sealed class DeviceStatus
{
    /// <summary>
    /// Source device identifier
    /// </summary>
    public required string SourceId { get; init; }

    /// <summary>
    /// Connection status
    /// </summary>
    public bool Connected { get; init; }

    /// <summary>
    /// Last seen timestamp
    /// </summary>
    public DateTimeOffset? LastSeen { get; init; }

    /// <summary>
    /// Last error message if any
    /// </summary>
    public string? LastError { get; init; }
}

