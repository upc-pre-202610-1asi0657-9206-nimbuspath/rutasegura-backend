using RutaSegura.Notification.Application.Model.Results;
using RutaSegura.Notification.Interfaces.Rest.Resources;

namespace RutaSegura.Notification.Interfaces.Rest.Transform;

public static class AnnouncementResourceFromResultAssembler
{
    public static AnnouncementResource ToResourceFromResult(CreateAnnouncementResult result) =>
        new(result.AnnouncementId, result.Created, result.NotificationsCreated);
}
