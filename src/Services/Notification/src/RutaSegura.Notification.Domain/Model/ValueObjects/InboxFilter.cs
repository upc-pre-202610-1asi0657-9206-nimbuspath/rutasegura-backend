namespace RutaSegura.Notification.Domain.Model.ValueObjects;

public sealed record InboxFilter(Guid OrganizationId, Guid RecipientUserId,
    IReadOnlyCollection<Guid> AuthorizedStudentIds, int Offset, int Limit, bool UnreadOnly);
