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
    }
}
