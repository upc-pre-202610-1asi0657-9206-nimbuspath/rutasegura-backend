using RutaSegura.Notification.Domain.Model.Aggregates;

namespace RutaSegura.Notification.Application.Model.Results;

public sealed record CreateAnnouncementResult(Guid AnnouncementId, bool Created, int NotificationsCreated);
