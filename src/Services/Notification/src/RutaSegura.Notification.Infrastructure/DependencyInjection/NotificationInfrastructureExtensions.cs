using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using RutaSegura.BuildingBlocks.Application.Persistence;
using RutaSegura.Notification.Application.Internal.OutboundServices;
using RutaSegura.Notification.Domain.Repositories;
using RutaSegura.Notification.Infrastructure.Persistence.EntityFrameworkCore.Configuration;
using RutaSegura.Notification.Infrastructure.Persistence.EntityFrameworkCore.Repositories;
using RutaSegura.Notification.Infrastructure.Security;

namespace RutaSegura.Notification.Infrastructure.DependencyInjection;

public static class NotificationInfrastructureExtensions
{
    public static IServiceCollection AddNotificationInfrastructure(this IServiceCollection services, IConfiguration? configuration = null)
    {
        services.TryAddSingleton(TimeProvider.System);
        var connection = configuration?.GetConnectionString("NotificationDatabase");
        if (!string.IsNullOrWhiteSpace(connection))
            services.AddNotificationPersistence(connection, configuration!["NotificationPersistence:PushTokenEncryptionKey"] ?? "");
        // Sin configuracion, el host solo ofrece liveness. Nunca se registran adaptadores ficticios.
        return services;
    }

    public static IServiceCollection AddNotificationPersistence(this IServiceCollection services, string connectionString, string encryptionKey)
    {
        NotificationDatabaseConnection.Validate(connectionString);
        var protector = new AesGcmPushTokenProtector(encryptionKey);
        services.TryAddSingleton<IPushTokenProtector>(protector);
        services.AddDbContext<NotificationDbContext>(options => options.UseMySQL(connectionString)
            .EnableSensitiveDataLogging(false));
        services.AddScoped<IUnitOfWork, NotificationUnitOfWork>();
        services.AddScoped<INotificationRepository, NotificationRepository>();
        services.AddScoped<IAnnouncementRepository, AnnouncementRepository>();
        services.AddScoped<IPushSubscriptionRepository, PushSubscriptionRepository>();
        services.AddScoped<IProcessedNotificationEventRepository, ProcessedNotificationEventRepository>();
        services.AddScoped<NotificationDispatchRepository>();
        services.AddScoped<INotificationDispatchRepository>(provider => provider.GetRequiredService<NotificationDispatchRepository>());
        services.AddScoped<INotificationDispatchQueue>(provider => provider.GetRequiredService<NotificationDispatchRepository>());
        return services;
    }
}
