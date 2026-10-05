using Microsoft.Extensions.DependencyInjection;
using RutaSegura.BuildingBlocks.Application.DependencyInjection;

namespace RutaSegura.Notification.Application.DependencyInjection;

public static class NotificationApplicationExtensions
{
    public static IServiceCollection AddNotificationApplication(this IServiceCollection services) =>
        services.AddRutaSeguraApplication(typeof(NotificationApplicationExtensions).Assembly);
}
