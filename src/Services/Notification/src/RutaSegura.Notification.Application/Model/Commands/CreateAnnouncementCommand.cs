using RutaSegura.BuildingBlocks.Application.Messaging;
using RutaSegura.Notification.Application.Model.Results;
using RutaSegura.Notification.Domain.Model.Aggregates;

namespace RutaSegura.Notification.Application.Model.Commands;

public sealed record CreateAnnouncementCommand(Guid RequestId, string Title, string Body, Guid? RouteId = null)
    : ICommand<CreateAnnouncementResult>;
