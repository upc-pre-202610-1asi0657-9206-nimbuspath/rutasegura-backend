using RutaSegura.Notification.Application.Model.Results;

namespace RutaSegura.Notification.Application.Internal.OutboundServices;

/// <summary>Obtenido del JWT ya validado (organizationId/sub/role/scopes), nunca del DTO del cliente.</summary>
public interface INotificationUserContext
{
    NotificationUser? Current { get; }
}
