using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using RutaSegura.Notification.Domain.Model.Aggregates;
using RutaSegura.Notification.Domain.Model.Entities;
using RutaSegura.Notification.Domain.Model.ValueObjects;
using RutaSegura.Notification.Infrastructure.Security;
using NotificationEntity = RutaSegura.Notification.Domain.Model.Aggregates.Notification;

namespace RutaSegura.Notification.Infrastructure.Persistence.EntityFrameworkCore.Configuration.Extensions;

public static class ModelBuilderExtensions
{
    public static void ApplyNotificationConfiguration(this ModelBuilder builder, IPushTokenProtector protector)
    {
        builder.Entity<Announcement>(entity =>
        {
            entity.ToTable("notification_announcements");
            entity.HasKey(a => a.Id);
            entity.HasAlternateKey(a => new { a.OrganizationId, a.Id });
            entity.Ignore(a => a.DomainEvents);
            entity.OwnsOne(a => a.Content, content =>
            {
                content.Property(c => c.Title).HasColumnName("title").HasMaxLength(NotificationContent.MaxTitleLength).IsRequired();
                content.Property(c => c.Body).HasColumnName("body").HasMaxLength(NotificationContent.MaxBodyLength).IsRequired();
            });
            entity.Navigation(a => a.Content).IsRequired();
            entity.HasIndex(a => new { a.OrganizationId, a.RequestId }).IsUnique().HasDatabaseName("ux_announcement_request");
        });

        builder.Entity<NotificationEntity>(entity =>
        {
            entity.ToTable("notification_notifications", table => table.HasCheckConstraint("ck_notification_subject",
                "`subject_id` = COALESCE(`student_id`, '00000000-0000-0000-0000-000000000000')"));
            entity.HasKey(n => n.Id);
            entity.HasAlternateKey(n => new { n.OrganizationId, n.Id });
            entity.Ignore(n => n.DomainEvents);
            entity.Ignore(n => n.Priority);
            entity.Ignore(n => n.Key);
            entity.Property(n => n.SourceMessageType).HasMaxLength(120).IsRequired();
            entity.Property(n => n.CorrelationId).HasMaxLength(200).IsRequired();
            entity.Property(n => n.Category).HasConversion<string>().HasMaxLength(24);
            entity.OwnsOne(n => n.Content, content =>
            {
                content.Property(c => c.Title).HasColumnName("title").HasMaxLength(NotificationContent.MaxTitleLength).IsRequired();
                content.Property(c => c.Body).HasColumnName("body").HasMaxLength(NotificationContent.MaxBodyLength).IsRequired();
            });
            entity.Navigation(n => n.Content).IsRequired();
            entity.HasIndex(n => new { n.OrganizationId, n.SourceMessageId, n.SourceMessageType, n.RecipientUserId, n.SubjectId })
                .IsUnique().HasDatabaseName("ux_notification_source_recipient_subject");
            entity.HasIndex(n => new { n.OrganizationId, n.RecipientUserId, n.CreatedAt, n.Id }).HasDatabaseName("ix_notification_inbox");
            entity.HasIndex(n => new { n.OrganizationId, n.RecipientUserId, n.ReadAt, n.CreatedAt }).HasDatabaseName("ix_notification_unread");
            entity.HasOne<Announcement>().WithMany().HasForeignKey(n => new { n.OrganizationId, n.AnnouncementId })
                .HasPrincipalKey(a => new { a.OrganizationId, a.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.Property<long>("Version").IsConcurrencyToken();
        });

        builder.Entity<PushSubscription>(entity =>
        {
            entity.ToTable("notification_push_subscriptions");
            entity.HasKey(s => s.Id);
            entity.Ignore(s => s.DomainEvents);
            entity.Property(s => s.Token).HasColumnName("token_ciphertext").HasColumnType("text")
                .HasConversion(new ValueConverter<string, string>(token => protector.Protect(token), value => protector.Unprotect(value))).IsRequired();
            entity.Property<string>("TokenHash").HasMaxLength(64).IsRequired();
            entity.Property(s => s.Platform).HasConversion<string>().HasMaxLength(16);
            entity.HasIndex(s => new { s.OrganizationId, s.InstallationId }).IsUnique().HasDatabaseName("ux_push_installation");
            entity.HasIndex("TokenHash").IsUnique().HasDatabaseName("ux_push_token_hash");
            entity.HasIndex(s => new { s.OrganizationId, s.UserId, s.IsEnabled }).HasDatabaseName("ix_push_recipient");
            entity.Property<long>("Version").IsConcurrencyToken();
        });

        builder.Entity<ProcessedNotificationEvent>(entity =>
        {
            entity.ToTable("notification_processed_events");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Consumer).HasMaxLength(120).IsRequired();
            entity.Property(e => e.MessageType).HasMaxLength(120).IsRequired();
            entity.Property(e => e.PayloadHash).HasMaxLength(64).IsRequired();
            entity.Property(e => e.SkippedReason).HasMaxLength(120);
            entity.HasIndex(e => new { e.Consumer, e.MessageId }).IsUnique().HasDatabaseName("ux_processed_consumer_message");
            entity.HasIndex(e => e.ProcessedAt).HasDatabaseName("ix_processed_at");
        });

        builder.Entity<NotificationDispatchJob>(entity =>
        {
            entity.ToTable("notification_dispatch_jobs");
            entity.HasKey(j => j.Id);
            entity.HasIndex(j => new { j.OrganizationId, j.NotificationId }).IsUnique().HasDatabaseName("ux_dispatch_notification");
            entity.HasIndex(j => new { j.ExpandedAt, j.CreatedAt, j.Id }).HasDatabaseName("ix_dispatch_pending");
            entity.HasOne<NotificationEntity>().WithMany().HasForeignKey(j => new { j.OrganizationId, j.NotificationId })
                .HasPrincipalKey(n => new { n.OrganizationId, n.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.Property<long>("Version").IsConcurrencyToken();
        });

        builder.Entity<NotificationDelivery>(entity =>
        {
            entity.ToTable("notification_deliveries");
            entity.HasKey(d => d.Id);
            entity.Property(d => d.Channel).HasConversion<string>().HasMaxLength(16);
            entity.Property(d => d.Status).HasConversion<string>().HasMaxLength(24);
            entity.Property(d => d.LastErrorCode).HasMaxLength(120);
            entity.Property(d => d.ProviderMessageId).HasMaxLength(512);
            entity.HasIndex(d => new { d.OrganizationId, d.NotificationId, d.Channel, d.DestinationId })
                .IsUnique().HasDatabaseName("ux_delivery_destination");
            entity.HasIndex(d => new { d.Status, d.NextAttemptAt, d.CreatedAt }).HasDatabaseName("ix_delivery_due");
            entity.HasOne<NotificationEntity>().WithMany().HasForeignKey(d => new { d.OrganizationId, d.NotificationId })
                .HasPrincipalKey(n => new { n.OrganizationId, n.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.Property<long>("Version").IsConcurrencyToken();
        });

        var utc = new ValueConverter<DateTimeOffset, DateTime>(value => value.UtcDateTime,
            value => new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc)));
        foreach (var entity in builder.Model.GetEntityTypes())
        foreach (var property in entity.GetProperties())
        {
            // Las columnas del owned Content se comparten con su tabla principal.
            if (entity.IsOwned() && property.IsPrimaryKey())
                property.SetColumnName("id");
            else if (property.Name is not ("Title" or "Body" or "Token"))
                property.SetColumnName(Regex.Replace(property.Name, "(?<!^)([A-Z])", "_$1").ToLowerInvariant());
            var type = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
            if (type == typeof(Guid))
            {
                property.SetValueConverter(new GuidToStringConverter());
                property.SetMaxLength(36);
                property.SetColumnType("varchar(36)");
                property.ValueGenerated = Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.Never;
            }
            else if (type == typeof(DateTimeOffset))
            {
                property.SetValueConverter(utc);
                property.SetColumnType("datetime(6)");
            }
            if (type == typeof(string) || type == typeof(Guid)) property.SetCollation("utf8mb4_bin");
        }
    }
}
