namespace RutaSegura.Notification.Application.Internal.OutboundServices;
/// <summary>Authenticated account before choosing an authorized operation; independent of its JWT tenant claim.</summary>
public interface INotificationAccountContext { Guid UserId { get; } bool IsParent { get; } Guid? OrganizationId { get; } }
