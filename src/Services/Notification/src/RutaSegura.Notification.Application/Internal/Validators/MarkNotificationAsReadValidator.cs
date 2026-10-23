using RutaSegura.BuildingBlocks.Application.Results;
using RutaSegura.BuildingBlocks.Application.Validation;
using RutaSegura.Notification.Application.Model.Commands;
using RutaSegura.Notification.Domain.Model.Aggregates;

namespace RutaSegura.Notification.Application.Internal.Validators;

internal sealed class MarkNotificationAsReadValidator : IValidator<MarkNotificationAsReadCommand>
{
    public IEnumerable<Error> Validate(MarkNotificationAsReadCommand command)
    {
        if (command.NotificationId == Guid.Empty)
            yield return Error.Validation("NOTIFICATION_ID_REQUIRED", "Se requiere el identificador del aviso.");
    }
}
