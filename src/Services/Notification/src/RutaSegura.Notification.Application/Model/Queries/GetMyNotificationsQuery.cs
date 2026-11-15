using RutaSegura.BuildingBlocks.Application.Messaging;
using RutaSegura.Notification.Application.Model.Results;
using RutaSegura.Notification.Domain.Model.Aggregates;

namespace RutaSegura.Notification.Application.Model.Queries;

// El cliente no elige UserId ni OrganizationId de la bandeja.
public sealed record GetMyNotificationsQuery(int Offset = 0, int Limit = 25, bool UnreadOnly = false)
    : IQuery<IReadOnlyList<NotificationResult>>;
