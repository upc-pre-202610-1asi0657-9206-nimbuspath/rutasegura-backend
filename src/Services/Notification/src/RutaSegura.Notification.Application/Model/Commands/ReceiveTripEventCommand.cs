using RutaSegura.BuildingBlocks.Application.Messaging;
using RutaSegura.Notification.Application.Model.Results;
using RutaSegura.Notification.Application.Contracts.V1;
using RutaSegura.Notification.Domain.Model.Aggregates;

namespace RutaSegura.Notification.Application.Model.Commands;

public sealed record ReceiveTripEventCommand(NotificationEventMetadata Metadata, TripNotificationEventV1 Event)
    : ICommand<ReceiveTripEventResult>;
