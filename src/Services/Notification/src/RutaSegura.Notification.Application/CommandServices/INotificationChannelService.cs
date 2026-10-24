namespace RutaSegura.Notification.Application.CommandServices;

/// <summary>Entrada exclusiva del worker: cada metodo confirma solo cambios locales, nunca retiene una transaccion durante I/O externo.</summary>
public interface INotificationChannelService
{
    Task ExpandAsync(Guid organizationId, Guid jobId, CancellationToken cancellationToken);
    Task DeliverAsync(Guid organizationId, Guid deliveryId, CancellationToken cancellationToken);
}
