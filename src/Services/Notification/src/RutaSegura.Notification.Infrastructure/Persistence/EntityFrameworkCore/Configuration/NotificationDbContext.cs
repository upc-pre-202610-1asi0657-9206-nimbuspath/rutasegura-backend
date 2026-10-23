using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using RutaSegura.BuildingBlocks.Domain;
using RutaSegura.Notification.Domain.Model.Aggregates;
using RutaSegura.Notification.Domain.Model.Entities;
using RutaSegura.Notification.Infrastructure.Persistence.EntityFrameworkCore.Configuration.Extensions;
using RutaSegura.Notification.Infrastructure.Security;
using NotificationEntity = RutaSegura.Notification.Domain.Model.Aggregates.Notification;

namespace RutaSegura.Notification.Infrastructure.Persistence.EntityFrameworkCore.Configuration;

public sealed class NotificationDbContext(DbContextOptions<NotificationDbContext> options, IPushTokenProtector tokenProtector)
    : DbContext(options)
{
    public DbSet<NotificationEntity> Notifications => Set<NotificationEntity>();
    public DbSet<Announcement> Announcements => Set<Announcement>();
    public DbSet<PushSubscription> PushSubscriptions => Set<PushSubscription>();
    public DbSet<ProcessedNotificationEvent> ProcessedEvents => Set<ProcessedNotificationEvent>();
    public DbSet<NotificationDispatchJob> DispatchJobs => Set<NotificationDispatchJob>();
    public DbSet<NotificationDelivery> Deliveries => Set<NotificationDelivery>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.ApplyNotificationConfiguration(tokenProtector);

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        PrepareChanges();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        PrepareChanges();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void PrepareChanges()
    {
        ChangeTracker.DetectChanges();
        // No perder silenciosamente eventos nuevos: el Outbox de publicacion se integra en la fase de mensajeria.
        if (ChangeTracker.Entries<IHasDomainEvents>().Any(e => e.Entity.DomainEvents.Count > 0))
            throw new InvalidOperationException("Se requiere un Outbox durable para confirmar agregados con eventos de dominio pendientes.");
        foreach (var entry in ChangeTracker.Entries().Where(e => e.State is EntityState.Added or EntityState.Modified))
        {
            if (entry.Metadata.FindProperty("Version") is not null)
                entry.Property("Version").CurrentValue = entry.State == EntityState.Added ? 1L :
                    checked((long)entry.Property("Version").OriginalValue! + 1L);
            if (entry.Entity is PushSubscription subscription)
                entry.Property("TokenHash").CurrentValue = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(subscription.Token)));
        }
    }
}
