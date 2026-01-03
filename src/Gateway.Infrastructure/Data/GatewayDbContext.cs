using Gateway.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace Gateway.Infrastructure.Data;

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
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Timestamp);
            entity.HasIndex(e => e.AdapterId);
            entity.HasIndex(e => e.SourceId);
            
            entity.Property(e => e.Id)
                .HasMaxLength(256);
            
            entity.Property(e => e.SourceId)
                .HasMaxLength(256)
                .IsRequired();
            
            entity.Property(e => e.AdapterId)
                .HasMaxLength(256)
                .IsRequired();
            
            entity.Property(e => e.Timestamp)
                .IsRequired();
            
            entity.Property(e => e.Payload)
                .HasColumnType("jsonb")
                .IsRequired();
            
            entity.Property(e => e.Metadata)
                .HasColumnType("jsonb");
        });
    }
}

