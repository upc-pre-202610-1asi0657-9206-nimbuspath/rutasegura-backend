using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RutaSegura.TripTracking.Infrastructure.Persistence.Records;

namespace RutaSegura.TripTracking.Infrastructure.Persistence.Configurations;

internal sealed class TrackPointConfiguration : IEntityTypeConfiguration<TrackPointRecord>
{
    public void Configure(EntityTypeBuilder<TrackPointRecord> builder)
    {
        builder.ToTable("track_points");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedOnAdd();
        builder.HasIndex(p => new { p.TripId, p.RecordedAt });
    }
}

internal sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("outbox_messages");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).ValueGeneratedNever();
        builder.Property(m => m.MessageType).HasMaxLength(120).IsRequired();
        builder.Property(m => m.Payload).HasColumnType("longtext").IsRequired();
        builder.Property(m => m.CorrelationId).HasMaxLength(64).IsRequired();
        builder.Property(m => m.LastError).HasMaxLength(1000);
        builder.HasIndex(m => new { m.ProcessedAt, m.NextAttemptAt, m.OccurredAt });
    }
}

internal sealed class InboxMessageConfiguration : IEntityTypeConfiguration<InboxMessage>
{
    public void Configure(EntityTypeBuilder<InboxMessage> builder)
    {
        builder.ToTable("inbox_messages");
        builder.HasKey(m => m.MessageId);
        builder.Property(m => m.MessageId).ValueGeneratedNever();
        builder.Property(m => m.MessageType).HasMaxLength(120).IsRequired();
    }
}

internal sealed class RouteSnapshotConfiguration : IEntityTypeConfiguration<RouteSnapshotRecord>
{
    public void Configure(EntityTypeBuilder<RouteSnapshotRecord> builder)
    {
        builder.ToTable("route_snapshots");
        builder.HasKey(r => r.RouteId);
        builder.Property(r => r.RouteId).ValueGeneratedNever();
        builder.Property(r => r.Name).HasMaxLength(120).IsRequired();
        builder.Property(r => r.Snapshot).HasColumnType("longtext").IsRequired();
    }
}

internal sealed class GuardianStudentLinkConfiguration : IEntityTypeConfiguration<GuardianStudentLink>
{
    public void Configure(EntityTypeBuilder<GuardianStudentLink> builder)
    {
        builder.ToTable("guardian_students");
        builder.HasKey(l => new { l.GuardianId, l.StudentId });
        builder.HasIndex(l => l.StudentId);
    }
}
