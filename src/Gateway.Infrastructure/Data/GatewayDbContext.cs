using Gateway.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace Gateway.Infrastructure.Data;

/// <summary>
/// DbContext for Gateway database operations
/// </summary>
public sealed class GatewayDbContext : DbContext
{
    public GatewayDbContext(DbContextOptions<GatewayDbContext> options)
        : base(options)
    {
    }

    public DbSet<TelemetryEventEntity> TelemetryEvents { get; set; } = null!;
    public DbSet<SensorAggregation10MinEntity> SensorAggregations10Min { get; set; } = null!;
    public DbSet<SensorAggregation1HourEntity> SensorAggregations1Hour { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Enable TimescaleDB extension
        modelBuilder.HasPostgresExtension("timescaledb");

        modelBuilder.Entity<TelemetryEventEntity>(entity =>
        {
            entity.ToTable("telemetry_events");

            // Composite Primary Key (event_id, timestamp) for TimescaleDB compatibility
            // TimescaleDB requires partitioning column (timestamp) to be part of primary key
            entity.HasKey(e => new { e.EventId, e.Timestamp });

            // Indexes for query performance
            entity.HasIndex(e => e.Timestamp)
                .HasDatabaseName("IX_telemetry_events_timestamp");

            entity.HasIndex(e => e.FactoryId)
                .HasDatabaseName("IX_telemetry_events_factory_id");

            entity.HasIndex(e => e.SourceId)
                .HasDatabaseName("IX_telemetry_events_source_id");

            entity.HasIndex(e => e.EquipmentType)
                .HasDatabaseName("IX_telemetry_events_equipment_type");

            entity.HasIndex(e => e.EquipmentName)
                .HasDatabaseName("IX_telemetry_events_equipment_name");

            entity.HasIndex(e => e.Tag)
                .HasDatabaseName("IX_telemetry_events_tag");

            // Composite indexes for common queries
            entity.HasIndex(e => new { e.FactoryId, e.Timestamp })
                .HasDatabaseName("IX_telemetry_events_factory_timestamp");

            entity.HasIndex(e => new { e.FactoryId, e.EquipmentType, e.Timestamp })
                .HasDatabaseName("IX_telemetry_events_factory_equipment_timestamp");

            entity.HasIndex(e => new { e.SourceId, e.Timestamp })
                .HasDatabaseName("IX_telemetry_events_source_timestamp");

            // Property configurations
            entity.Property(e => e.EventId)
                .IsRequired();

            entity.Property(e => e.Timestamp)
                .IsRequired();

            entity.Property(e => e.FactoryId)
                .IsRequired()
                .HasConversion<int>(); // Store enum as int

            entity.Property(e => e.SourceId)
                .HasMaxLength(256)
                .IsRequired();

            entity.Property(e => e.EquipmentType)
                .HasMaxLength(256)
                .IsRequired();

            entity.Property(e => e.EquipmentName)
                .HasMaxLength(256)
                .IsRequired();

            entity.Property(e => e.Tag)
                .HasMaxLength(256)
                .IsRequired();

            entity.Property(e => e.Sequence)
                .IsRequired();

            entity.Property(e => e.Quality)
                .IsRequired()
                .HasConversion<int>(); // Store enum as int

            entity.Property(e => e.ValueJson)
                .HasColumnType("jsonb")
                .IsRequired();

            entity.Property(e => e.RouteKey)
                .HasMaxLength(50)
                .IsRequired();

            entity.Property(e => e.TraceId)
                .HasMaxLength(256);
        });

        // Configure 10-minute aggregation table
        modelBuilder.Entity<SensorAggregation10MinEntity>(entity =>
        {
            entity.ToTable("sensor_agg_10min");

            // Composite Primary Key
            entity.HasKey(e => new { e.Bucket, e.FactoryId, e.Tag, e.EquipmentType, e.EquipmentName, e.SourceId });

            // Indexes for query performance
            entity.HasIndex(e => new { e.FactoryId, e.Tag, e.Bucket })
                .HasDatabaseName("IX_sensor_agg_10min_factory_tag_bucket")
                .IsDescending(false, false, true);

            entity.HasIndex(e => e.Bucket)
                .HasDatabaseName("IX_sensor_agg_10min_bucket")
                .IsDescending(true);

            entity.HasIndex(e => e.LastTimestamp)
                .HasDatabaseName("IX_sensor_agg_10min_last_timestamp")
                .IsDescending(true);

            // Property configurations
            entity.Property(e => e.Bucket)
                .IsRequired();

            entity.Property(e => e.FactoryId)
                .IsRequired()
                .HasConversion<int>();

            entity.Property(e => e.Tag)
                .HasMaxLength(256)
                .IsRequired();

            entity.Property(e => e.EquipmentType)
                .HasMaxLength(256)
                .IsRequired();

            entity.Property(e => e.EquipmentName)
                .HasMaxLength(256)
                .IsRequired();

            entity.Property(e => e.SourceId)
                .HasMaxLength(256)
                .IsRequired();

            entity.Property(e => e.AvgValue)
                .HasColumnType("numeric")
                .IsRequired();

            entity.Property(e => e.MinValue)
                .HasColumnType("numeric")
                .IsRequired();

            entity.Property(e => e.MaxValue)
                .HasColumnType("numeric")
                .IsRequired();

            entity.Property(e => e.Count)
                .IsRequired();

            entity.Property(e => e.LastTimestamp)
                .IsRequired();
        });

        // Configure 1-hour aggregation table
        modelBuilder.Entity<SensorAggregation1HourEntity>(entity =>
        {
            entity.ToTable("sensor_agg_1hour");

            // Composite Primary Key
            entity.HasKey(e => new { e.Bucket, e.FactoryId, e.Tag, e.EquipmentType, e.EquipmentName, e.SourceId });

            // Indexes for query performance
            entity.HasIndex(e => new { e.FactoryId, e.Tag, e.Bucket })
                .HasDatabaseName("IX_sensor_agg_1hour_factory_tag_bucket")
                .IsDescending(false, false, true);

            entity.HasIndex(e => e.Bucket)
                .HasDatabaseName("IX_sensor_agg_1hour_bucket")
                .IsDescending(true);

            // Property configurations
            entity.Property(e => e.Bucket)
                .IsRequired();

            entity.Property(e => e.FactoryId)
                .IsRequired()
                .HasConversion<int>();

            entity.Property(e => e.Tag)
                .HasMaxLength(256)
                .IsRequired();

            entity.Property(e => e.EquipmentType)
                .HasMaxLength(256)
                .IsRequired();

            entity.Property(e => e.EquipmentName)
                .HasMaxLength(256)
                .IsRequired();

            entity.Property(e => e.SourceId)
                .HasMaxLength(256)
                .IsRequired();

            entity.Property(e => e.AvgValue)
                .HasColumnType("numeric")
                .IsRequired();

            entity.Property(e => e.MinValue)
                .HasColumnType("numeric")
                .IsRequired();

            entity.Property(e => e.MaxValue)
                .HasColumnType("numeric")
                .IsRequired();

            entity.Property(e => e.Count)
                .IsRequired();
        });
    }
}
