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

        modelBuilder.Entity<TelemetryEventEntity>(entity =>
        {
            entity.ToTable("telemetry_events");

            // Primary Key
            entity.HasKey(e => e.EventId);

            // Unique constraint on EventId (already enforced by PK, but explicit for clarity)
            entity.HasIndex(e => e.EventId)
                .IsUnique();

            // Indexes for query performance
            entity.HasIndex(e => e.Timestamp)
                .HasDatabaseName("IX_telemetry_events_timestamp");

            entity.HasIndex(e => e.SourceId)
                .HasDatabaseName("IX_telemetry_events_source_id");

            entity.HasIndex(e => e.Tag)
                .HasDatabaseName("IX_telemetry_events_tag");

            // Composite index for common queries (source + timestamp)
            entity.HasIndex(e => new { e.SourceId, e.Timestamp })
                .HasDatabaseName("IX_telemetry_events_source_timestamp");

            // Property configurations
            entity.Property(e => e.EventId)
                .IsRequired();

            entity.Property(e => e.Timestamp)
                .IsRequired();

            entity.Property(e => e.SourceId)
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
