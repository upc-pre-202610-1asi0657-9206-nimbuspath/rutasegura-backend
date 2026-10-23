using RutaSegura.BuildingBlocks.Application.Messaging;
using RutaSegura.BuildingBlocks.Application.Results;
using RutaSegura.Notification.Application.CommandServices;
using RutaSegura.Notification.Application.Model.Commands;
using RutaSegura.Notification.Application.Model.Results;

namespace RutaSegura.Notification.Application.Internal.CommandServices;

/// <summary>Entrada a los comandos. Conserva Logging, Validation y Transaction del Dispatcher compartido.</summary>
internal sealed class NotificationCommandService(IDispatcher dispatcher) : INotificationCommandService
{
    public Task<Result<ReceiveTripEventResult>> Handle(ReceiveTripEventCommand command, CancellationToken cancellationToken = default) =>
        dispatcher.SendAsync(command, cancellationToken);

    public Task<Result<CreateAnnouncementResult>> Handle(CreateAnnouncementCommand command, CancellationToken cancellationToken = default) =>
        dispatcher.SendAsync(command, cancellationToken);

    public Task<Result<NotificationResult>> Handle(MarkNotificationAsReadCommand command, CancellationToken cancellationToken = default) =>
        dispatcher.SendAsync(command, cancellationToken);
}
