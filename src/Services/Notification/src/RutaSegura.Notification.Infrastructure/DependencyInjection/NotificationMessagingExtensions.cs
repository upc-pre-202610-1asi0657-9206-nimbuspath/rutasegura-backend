using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RutaSegura.Notification.Infrastructure.Messaging.Inbound;
using RutaSegura.Notification.Infrastructure.Messaging.RabbitMq;

namespace RutaSegura.Notification.Infrastructure.DependencyInjection;

public static class NotificationMessagingExtensions
{
    /// <summary>Los puertos de Application y la persistencia deben registrarse en el composition root.</summary>
    public static IServiceCollection AddNotificationRabbitMq(this IServiceCollection services, NotificationRabbitMqOptions options)
    {
        options.Validate();
        services.AddSingleton(options);
        services.TryAddSingleton<TripTrackingMessageDecoder>();
        services.TryAddSingleton<INotificationMessageProcessor, NotificationMessageProcessor>();
        services.TryAddSingleton<NotificationDeliveryCoordinator>();
        services.TryAddSingleton<NotificationRabbitMqConnection>();
        services.TryAddSingleton<NotificationRabbitMqConsumer>();
        services.AddHostedService(provider => provider.GetRequiredService<NotificationRabbitMqConsumer>());
        return services;
    }
}
