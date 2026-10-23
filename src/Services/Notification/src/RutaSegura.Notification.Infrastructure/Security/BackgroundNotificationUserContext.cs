using RutaSegura.Notification.Application.Internal.OutboundServices;
using RutaSegura.Notification.Application.Model.Results;

namespace RutaSegura.Notification.Infrastructure.Security;

/// <summary>Un worker no es un usuario. Los casos de uso REST deniegan el acceso hasta incorporar JWT.</summary>
internal sealed class BackgroundNotificationUserContext : INotificationUserContext
{
    public NotificationUser? Current => null;
}
