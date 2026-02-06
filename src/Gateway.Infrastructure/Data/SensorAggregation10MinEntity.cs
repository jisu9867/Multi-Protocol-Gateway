using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Gateway.Core.Models;

namespace Gateway.Infrastructure.Data;

/// <summary>
/// Entity for 10-minute aggregated sensor readings
/// </summary>
[Table("sensor_agg_10min")]
public sealed class SensorAggregation10MinEntity
{
    /// <summary>
    /// Composite primary key: bucket + factory_id + tag + equipment_type + equipment_name + source_id
    /// </summary>
    [Column("bucket")]
    public DateTime Bucket { get; set; }

    [Column("factory_id")]
    public Factory FactoryId { get; set; }

    [Required]
    [MaxLength(256)]
    [Column("tag")]
    public string Tag { get; set; } = string.Empty;

    [Required]
    [MaxLength(256)]
    [Column("equipment_type")]
    public string EquipmentType { get; set; } = string.Empty;

    [Required]
    [MaxLength(256)]
    [Column("equipment_name")]
    public string EquipmentName { get; set; } = string.Empty;

    [Required]
    [MaxLength(256)]
    [Column("source_id")]
    public string SourceId { get; set; } = string.Empty;

    /// <summary>
    /// Average value for this bucket
    /// </summary>
    [Required]
    [Column("avg_value", TypeName = "numeric")]
    public decimal AvgValue { get; set; }

    /// <summary>
    /// Minimum value for this bucket
    /// </summary>
    [Required]
    [Column("min_value", TypeName = "numeric")]
    public decimal MinValue { get; set; }

    /// <summary>
    /// Maximum value for this bucket
    /// </summary>
    [Required]
    [Column("max_value", TypeName = "numeric")]
    public decimal MaxValue { get; set; }

    /// <summary>
    /// Count of events in this bucket
    /// </summary>
    [Required]
    [Column("count")]
    public long Count { get; set; }

    /// <summary>
    /// Last timestamp in this bucket
    /// </summary>
    [Required]
    [Column("last_timestamp")]
    public DateTime LastTimestamp { get; set; }
}

