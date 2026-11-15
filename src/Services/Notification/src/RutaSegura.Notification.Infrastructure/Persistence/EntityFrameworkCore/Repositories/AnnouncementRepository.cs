using Microsoft.EntityFrameworkCore;
using RutaSegura.Notification.Domain.Model.Aggregates;
using RutaSegura.Notification.Domain.Repositories;
using RutaSegura.Notification.Infrastructure.Persistence.EntityFrameworkCore.Configuration;

namespace RutaSegura.Notification.Infrastructure.Persistence.EntityFrameworkCore.Repositories;

public sealed class AnnouncementRepository(NotificationDbContext context) : IAnnouncementRepository
{
    public Task<Announcement?> FindByRequestAsync(Guid organizationId, Guid requestId, CancellationToken cancellationToken) =>
        context.Announcements.AsNoTracking().SingleOrDefaultAsync(a => a.OrganizationId == organizationId && a.RequestId == requestId, cancellationToken);
    public void Add(Announcement announcement) => context.Announcements.Add(announcement);
}
