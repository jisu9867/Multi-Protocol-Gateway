using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Gateway.Core.Models;
using Gateway.Core.Pipeline;

namespace Gateway.Infrastructure.Data;

/// <summary>
/// Entity for telemetry events stored in PostgreSQL
/// </summary>
[Table("telemetry_events")]
public sealed class TelemetryEventEntity
{
    /// <summary>
    /// Unique event identifier (Primary Key)
    /// </summary>
    [Key]
    [Column("event_id")]
    public Guid EventId { get; set; }

    /// <summary>
    /// Event timestamp (UTC)
    /// </summary>
    [Required]
    [Column("timestamp")]
    public DateTime Timestamp { get; set; }

    /// <summary>
    /// Factory identifier
    /// </summary>
    [Required]
    [Column("factory_id")]
    public Factory FactoryId { get; set; }

    /// <summary>
    /// Source device/entity identifier
    /// </summary>
    [Required]
    [MaxLength(256)]
    [Column("source_id")]
    public string SourceId { get; set; } = string.Empty;

    /// <summary>
    /// Equipment type
    /// </summary>
    [Required]
    [MaxLength(256)]
    [Column("equipment_type")]
    public string EquipmentType { get; set; } = string.Empty;

    /// <summary>
    /// Equipment name
    /// </summary>
    [Required]
    [MaxLength(256)]
    [Column("equipment_name")]
    public string EquipmentName { get; set; } = string.Empty;

    /// <summary>
    /// Tag for categorizing the event
    /// </summary>
    [Required]
    [MaxLength(256)]
    [Column("tag")]
    public string Tag { get; set; } = string.Empty;

    /// <summary>
    /// Sequence number for ordering events from the same source
    /// </summary>
    [Column("sequence")]
    public long Sequence { get; set; }

    /// <summary>
    /// Data quality indicator
    /// </summary>
    [Required]
    [Column("quality")]
    public DataQuality Quality { get; set; }

    /// <summary>
    /// Event value stored as JSONB
    /// </summary>
    [Required]
    [Column("value_json", TypeName = "jsonb")]
    public string ValueJson { get; set; } = string.Empty;

    /// <summary>
    /// Route key indicating which route this event belongs to
    /// </summary>
    [Required]
    [MaxLength(50)]
    [Column("route_key")]
    public string RouteKey { get; set; } = string.Empty;

    /// <summary>
    /// Optional trace identifier for distributed tracing
    /// </summary>
    [MaxLength(256)]
    [Column("trace_id")]
    public string? TraceId { get; set; }
}
