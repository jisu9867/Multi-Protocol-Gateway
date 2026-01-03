namespace Gateway.Core.Models;

/// <summary>
/// Canonical Data Model for telemetry events
/// </summary>
public sealed class TelemetryEvent
{
    public required string Id { get; init; }
    public required string SourceId { get; init; }
    public required string AdapterId { get; init; }
    public required DateTime Timestamp { get; init; }
    public required Dictionary<string, object?> Payload { get; init; }
    public Dictionary<string, string> Metadata { get; init; } = new();
}

