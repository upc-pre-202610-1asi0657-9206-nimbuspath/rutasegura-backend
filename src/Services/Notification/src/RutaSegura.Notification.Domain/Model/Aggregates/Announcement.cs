using RutaSegura.BuildingBlocks.Domain;
using RutaSegura.Notification.Domain.Model.ValueObjects;

namespace RutaSegura.Notification.Domain.Model.Aggregates;

public sealed class Announcement : AggregateRoot<Guid>
{
    private Announcement() { }
    public Guid OrganizationId { get; private set; }
    public Guid AuthorUserId { get; private set; }
    public Guid RequestId { get; private set; }
    public Guid? RouteId { get; private set; }
    public NotificationContent Content { get; private set; } = null!;
    public DateTimeOffset CreatedAt { get; private set; }

    public static Announcement Create(Guid organizationId, Guid authorUserId, Guid requestId,
        Guid? routeId, NotificationContent content, DateTimeOffset now)
    {
        if (organizationId == Guid.Empty || authorUserId == Guid.Empty || requestId == Guid.Empty ||
            routeId == Guid.Empty || content is null || now == default)
            throw new DomainException("ANNOUNCEMENT_INVALID_DATA", "El comunicado requiere organizacion, autor, solicitud y contenido validos.");
        return new Announcement
        {
            Id = Guid.CreateVersion7(), OrganizationId = organizationId, AuthorUserId = authorUserId,
            RequestId = requestId, RouteId = routeId, Content = content, CreatedAt = now.ToUniversalTime()
        };
    }
}
