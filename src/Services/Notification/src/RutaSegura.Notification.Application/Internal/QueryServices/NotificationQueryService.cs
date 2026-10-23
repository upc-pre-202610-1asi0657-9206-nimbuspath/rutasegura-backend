using RutaSegura.BuildingBlocks.Application.Messaging;
using RutaSegura.BuildingBlocks.Application.Results;
using RutaSegura.Notification.Application.Model.Queries;
using RutaSegura.Notification.Application.Model.Results;
using RutaSegura.Notification.Application.QueryServices;

namespace RutaSegura.Notification.Application.Internal.QueryServices;

/// <summary>Entrada a las consultas. No abre una transaccion de escritura.</summary>
internal sealed class NotificationQueryService(IDispatcher dispatcher) : INotificationQueryService
{
    public Task<Result<IReadOnlyList<NotificationResult>>> Handle(GetMyNotificationsQuery query, CancellationToken cancellationToken = default) =>
        dispatcher.QueryAsync(query, cancellationToken);

    public Task<Result<NotificationResult>> Handle(GetMyNotificationQuery query, CancellationToken cancellationToken = default) =>
        dispatcher.QueryAsync(query, cancellationToken);
}
