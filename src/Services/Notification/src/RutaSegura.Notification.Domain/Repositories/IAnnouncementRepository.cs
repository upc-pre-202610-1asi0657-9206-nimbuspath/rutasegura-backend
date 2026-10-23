using RutaSegura.Notification.Domain.Model.Aggregates;

namespace RutaSegura.Notification.Domain.Repositories;

public interface IAnnouncementRepository
{
    Task<Announcement?> FindByRequestAsync(Guid organizationId, Guid requestId, CancellationToken cancellationToken);
    void Add(Announcement announcement);
}
