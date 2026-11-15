using RutaSegura.BuildingBlocks.Application.Results;
using RutaSegura.BuildingBlocks.Application.Validation;
using RutaSegura.Notification.Application.Model.Commands;
using RutaSegura.Notification.Domain.Model.Aggregates;
using RutaSegura.Notification.Domain.Model.ValueObjects;

namespace RutaSegura.Notification.Application.Internal.Validators;

internal sealed class CreateAnnouncementValidator : IValidator<CreateAnnouncementCommand>
{
    public IEnumerable<Error> Validate(CreateAnnouncementCommand command)
    {
        if (command.RequestId == Guid.Empty || command.RouteId == Guid.Empty)
            yield return Error.Validation("ANNOUNCEMENT_INVALID_REQUEST", "Se requiere requestId y una ruta valida cuando corresponda.");
        if (string.IsNullOrWhiteSpace(command.Title) || command.Title.Trim().Length > NotificationContent.MaxTitleLength ||
            string.IsNullOrWhiteSpace(command.Body) || command.Body.Trim().Length > NotificationContent.MaxBodyLength)
            yield return Error.Validation("NOTIFICATION_INVALID_CONTENT", "El titulo y el contenido son obligatorios y deben respetar sus limites.");
    }
}
