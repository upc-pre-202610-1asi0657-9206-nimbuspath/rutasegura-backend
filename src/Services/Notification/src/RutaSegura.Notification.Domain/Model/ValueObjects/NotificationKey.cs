namespace RutaSegura.Notification.Domain.Model.ValueObjects;

/// <summary>Identifica un aviso logico por hecho, destinatario y alumno; debe tener un indice unico en MySQL.</summary>
public readonly record struct NotificationKey(
    Guid OrganizationId, Guid SourceMessageId, string SourceMessageType, Guid RecipientUserId, Guid SubjectId);
