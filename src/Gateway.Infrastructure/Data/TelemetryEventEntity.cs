using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Gateway.Infrastructure.Data;

[Table("telemetry_events")]
public sealed class TelemetryEventEntity
{
    [Key]
    [MaxLength(256)]
    public string Id { get; set; } = string.Empty;

    [Required]
    [MaxLength(256)]
    public string SourceId { get; set; } = string.Empty;

    [Required]
    [MaxLength(256)]
    public string AdapterId { get; set; } = string.Empty;

    [Required]
    public DateTime Timestamp { get; set; }

    [Required]
    [Column(TypeName = "jsonb")]
    public Dictionary<string, object?> Payload { get; set; } = new();

    [Column(TypeName = "jsonb")]
    public Dictionary<string, string> Metadata { get; set; } = new();
}

