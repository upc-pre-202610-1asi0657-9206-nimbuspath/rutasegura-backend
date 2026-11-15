using RutaSegura.BuildingBlocks.Application.Messaging;
using RutaSegura.Notification.Application.Model.Results;

namespace RutaSegura.Notification.Application.Model.Queries;

public sealed record GetMyNotificationPageQuery(string? Cursor = null, int Limit = 25, bool UnreadOnly = false) : IQuery<NotificationPageResult>
{
    public override string ToString() => $"GetMyNotificationPageQuery {{ Cursor = [REDACTED], Limit = {Limit}, UnreadOnly = {UnreadOnly} }}";
}
