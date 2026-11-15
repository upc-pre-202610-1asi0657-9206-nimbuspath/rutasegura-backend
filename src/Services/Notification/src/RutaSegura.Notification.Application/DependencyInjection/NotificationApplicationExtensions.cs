using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RutaSegura.BuildingBlocks.Application.DependencyInjection;
using RutaSegura.BuildingBlocks.Application.Messaging;
using RutaSegura.Notification.Application.Internal.Policies;
using RutaSegura.Notification.Application.CommandServices;
using RutaSegura.Notification.Application.QueryServices;
using RutaSegura.Notification.Application.Internal.CommandServices;
using RutaSegura.Notification.Application.Internal.QueryServices;

namespace RutaSegura.Notification.Application.DependencyInjection;

public static class NotificationApplicationExtensions
{
    public static IServiceCollection AddNotificationChannelApplication(this IServiceCollection services, NotificationChannelPolicy policy)
    {
        services.AddSingleton(policy);
        services.AddScoped<INotificationChannelService, NotificationChannelService>();
        return services;
    }

    /// <summary>Activa los casos de uso cuando Infrastructure aporta los puertos reales. Nunca registra fakes.</summary>
    public static IServiceCollection AddNotificationApplication(this IServiceCollection services, bool registerUseCases = true)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton(NotificationProcessingPolicy.Default);
        services.TryAddSingleton(new NotificationReplayPolicy());
        services.TryAddSingleton(provider => new NotificationCursorCodec(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32), provider.GetRequiredService<TimeProvider>()));
        if (registerUseCases)
        {
            services.AddScoped<NotificationInboxReader>();
            services.AddScoped<INotificationDeliveryCommandService, NotificationDeliveryCommandService>();
            services.AddScoped<INotificationDeliveryQueryService, NotificationDeliveryQueryService>();
            services.AddRutaSeguraApplication(typeof(NotificationApplicationExtensions).Assembly);
            services.AddScoped<INotificationCommandService, NotificationCommandService>();
            services.AddScoped<INotificationQueryService, NotificationQueryService>();
        }
        else
            services.AddScoped<IDispatcher, Dispatcher>();
        return services;
    }
}
