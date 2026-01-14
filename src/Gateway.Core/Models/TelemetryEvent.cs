using System.Text.Json;

namespace Gateway.Core.Models;

/// <summary>
/// Canonical Data Model for telemetry events
/// </summary>
public sealed class TelemetryEvent
{
    /// <summary>
    /// Unique event identifier (Guid)
    /// </summary>
    public required Guid EventId { get; init; }

    /// <summary>
    /// Event timestamp with timezone information
    /// </summary>
    public required DateTimeOffset Timestamp { get; init; }

    /// <summary>
    /// Factory identifier
    /// </summary>
    public required Factory FactoryId { get; init; }

    /// <summary>
    /// Source device/entity identifier
    /// </summary>
    public required string SourceId { get; init; }

    /// <summary>
    /// Equipment type
    /// </summary>
    public required string EquipmentType { get; init; }

    /// <summary>
    /// Equipment name
    /// </summary>
    public required string EquipmentName { get; init; }

    /// <summary>
    /// Tag for categorizing the event
    /// </summary>
    public required string Tag { get; init; }

    /// <summary>
    /// Event value (JsonElement for flexible JSON handling, or object for typed data)
    /// </summary>
    public JsonElement Value { get; init; }

    /// <summary>
    /// Data quality indicator
    /// </summary>
    public DataQuality Quality { get; init; } = DataQuality.Good;

    /// <summary>
    /// Sequence number for ordering events from the same source
    /// </summary>
    public long Sequence { get; init; }

    /// <summary>
    /// Optional trace identifier for distributed tracing
    /// </summary>
    public string? TraceId { get; init; }
}

/// <summary>
/// Data quality enumeration
/// </summary>
public enum DataQuality
{
    /// <summary>
    /// Good quality data
    /// </summary>
    Good = 0,

    /// <summary>
    /// Uncertain quality data
    /// </summary>
    Uncertain = 1,

    /// <summary>
    /// Bad quality data
    /// </summary>
    Bad = 2,

    /// <summary>
    /// Data quality unknown
    /// </summary>
    Unknown = 3
}
