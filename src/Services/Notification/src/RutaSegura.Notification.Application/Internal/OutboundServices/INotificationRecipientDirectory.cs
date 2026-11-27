using RutaSegura.BuildingBlocks.Application.Results;
using RutaSegura.Notification.Application.Model.Results;

namespace RutaSegura.Notification.Application.Internal.OutboundServices;

/// <summary>Los adaptadores consultan Roster/Administration por contratos protegidos, nunca sus tablas.
/// Deben fallar ante indisponibilidad: una lista vacia solo significa que no hay destinatarios autorizados.</summary>
public interface INotificationRecipientDirectory
{
    /// <summary>Verified DRIVER accounts, optionally scoped to a route or the IAM user on a trip.</summary>
    Task<IReadOnlyList<NotificationRecipient>> ForDriversAsync(Guid organizationId, Guid? driverUserId, Guid? routeId,
        CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<NotificationRecipient>>([]);
    Task<IReadOnlyCollection<Guid>> CurrentOrganizationIdsAsync(Guid userId, CancellationToken ct) => Task.FromResult<IReadOnlyCollection<Guid>>([]);

    Task<IReadOnlyList<NotificationRecipient>> ForStudentsAsync(Guid organizationId,
        IReadOnlyCollection<Guid> studentIds, CancellationToken cancellationToken);
    Task<IReadOnlyList<NotificationRecipient>> OperatorsAsync(Guid organizationId, CancellationToken cancellationToken);
    /// <summary>Valida tambien que RouteId pertenezca a la organizacion; rechaza rutas inexistentes o ajenas.</summary>
    Task<Result<IReadOnlyList<NotificationRecipient>>> ForAnnouncementAsync(Guid organizationId, Guid? routeId,
        CancellationToken cancellationToken);
    Task<IReadOnlyCollection<Guid>> CurrentStudentLinksAsync(Guid organizationId, Guid userId,
        CancellationToken cancellationToken);
}
