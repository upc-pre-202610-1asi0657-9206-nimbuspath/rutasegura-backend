using RutaSegura.Notification.Application.Model.Commands;
using RutaSegura.Notification.Interfaces.Rest.Resources;

namespace RutaSegura.Notification.Interfaces.Rest.Transform;

public static class CreateAnnouncementCommandFromResourceAssembler
{
    public static CreateAnnouncementCommand ToCommandFromResource(CreateAnnouncementResource resource) =>
        new(resource.RequestId, resource.Title, resource.Body, resource.RouteId);
}
