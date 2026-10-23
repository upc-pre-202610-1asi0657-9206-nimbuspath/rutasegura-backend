using RutaSegura.BuildingBlocks.Application.Messaging;
using RutaSegura.Notification.Application.Model.Results;
using RutaSegura.Notification.Domain.Model.Aggregates;

namespace RutaSegura.Notification.Application.Model.Queries;

public sealed record GetMyNotificationQuery(Guid NotificationId) : IQuery<NotificationResult>;
