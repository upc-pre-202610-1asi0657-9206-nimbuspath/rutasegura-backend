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
using RutaSegura.Notification.Infrastructure.Integration;
using RutaSegura.Notification.Infrastructure.Messaging.RabbitMq;
using RutaSegura.Notification.Infrastructure.Channels;
using RutaSegura.Notification.Infrastructure.Channels.Fcm;
using RutaSegura.Notification.Application.Internal.Policies;
using RutaSegura.Notification.Application.DependencyInjection;
using RutaSegura.Notification.Application.Internal.QueryServices;
using RutaSegura.Notification.Infrastructure.Persistence.EntityFrameworkCore.Maintenance;

namespace RutaSegura.Notification.Infrastructure.DependencyInjection;

public static class NotificationInfrastructureExtensions
{
    public static IServiceCollection AddNotificationInfrastructure(this IServiceCollection services, IConfiguration? configuration = null)
    {
        services.TryAddSingleton(TimeProvider.System);
        var connection = configuration?.GetConnectionString("NotificationDatabase");
        if (!string.IsNullOrWhiteSpace(connection))
            services.AddNotificationPersistence(connection, configuration!["NotificationPersistence:PushTokenEncryptionKey"] ?? "");
        var messaging = configuration?.GetValue<bool>("NotificationRabbitMq:Enabled") == true;
        var api = configuration?.GetValue<bool>("NotificationApi:Enabled") == true;
        var channels = configuration?.GetValue<bool>("NotificationChannels:Enabled") == true;
        if (messaging || api || channels)
        {
            if (string.IsNullOrWhiteSpace(connection))
                throw new InvalidOperationException("Notification requiere NotificationDatabase y sus migraciones aplicadas.");
            var dependencies = configuration!.GetSection(NotificationDependencyOptions.SectionName).Get<NotificationDependencyOptions>() ?? new();
            AddClient<INotificationRecipientDirectory, AdministrationRecipientDirectory>(services, dependencies.Administration);
            AddClient<ITripNotificationContext, TripNotificationContextClient>(services, dependencies.TripTracking);
            services.TryAddScoped<INotificationUserContext, BackgroundNotificationUserContext>();
            var replayLimit = configuration.GetValue("NotificationReplay:MaxManualReplays", 3);
            if (replayLimit is < 1 or > 10) throw new InvalidOperationException("NotificationReplay:MaxManualReplays debe estar entre 1 y 10.");
            services.Replace(ServiceDescriptor.Singleton(new NotificationReplayPolicy(replayLimit)));
            var retention = configuration.GetSection("NotificationRetention").Get<NotificationRetentionOptions>() ?? new();
            retention.Validate();
            if (retention.Enabled)
            {
                services.AddSingleton(retention);
                services.TryAddSingleton(new MySqlDispatchLock(connection!));
                services.AddScoped<NotificationRetentionStore>();
                services.AddHostedService<NotificationRetentionWorker>();
            }
            if (messaging) services.AddNotificationRabbitMq(configuration.GetSection(NotificationRabbitMqOptions.Section).Get<NotificationRabbitMqOptions>() ?? new());
            if (channels)
            {
                if (!api) throw new InvalidOperationException("Los canales requieren NotificationApi:Enabled para habilitar el hub autenticado.");
                var channelOptions = configuration.GetSection("NotificationChannels").Get<NotificationChannelOptions>() ?? new();
                channelOptions.Validate();
                var fcmOptions = configuration.GetSection("NotificationFcm").Get<NotificationFcmOptions>() ?? new();
                if (fcmOptions.Enabled && string.IsNullOrWhiteSpace(fcmOptions.ProjectId))
                    throw new InvalidOperationException("Configura NotificationFcm:ProjectId antes de habilitar FCM.");
                services.AddSingleton(channelOptions);
                services.AddSingleton(fcmOptions);
                services.AddNotificationChannelApplication(new NotificationChannelPolicy(fcmOptions.Enabled, channelOptions.MaxAttempts));
                services.AddSingleton<IFcmTransport, FcmSdkTransport>();
                services.AddSingleton<IPushNotificationSender, FcmPushNotificationSender>();
                services.TryAddSingleton(new MySqlDispatchLock(connection!));
                services.AddSingleton<NotificationChannelWorker>();
                services.AddHostedService(provider => provider.GetRequiredService<NotificationChannelWorker>());
            }
        }
        // Sin configuracion, el host solo ofrece liveness. Nunca se registran adaptadores ficticios.
        return services;
    }

    private static void AddClient<TPort, TAdapter>(IServiceCollection services, NotificationDependencyOptions.ServiceEndpoint endpoint)
        where TPort : class where TAdapter : class, TPort
    {
        var uri = endpoint.Validate();
        services.AddHttpClient<TPort, TAdapter>(client =>
        {
            client.BaseAddress = uri;
            client.Timeout = TimeSpan.FromSeconds(10);
            client.MaxResponseContentBufferSize = 1024 * 1024;
            client.DefaultRequestHeaders.Add("X-Service-Token", endpoint.ServiceToken);
        }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
    }

    public static IServiceCollection AddNotificationPersistence(this IServiceCollection services, string connectionString, string encryptionKey)
    {
        NotificationDatabaseConnection.Validate(connectionString);
        var protector = new AesGcmPushTokenProtector(encryptionKey);
        services.TryAddSingleton(TimeProvider.System);
        services.Replace(ServiceDescriptor.Singleton(provider => new NotificationCursorCodec(
            System.Security.Cryptography.HMACSHA256.HashData(Convert.FromBase64String(encryptionKey), System.Text.Encoding.UTF8.GetBytes("rutasegura.notification.cursor.v1")),
            provider.GetRequiredService<TimeProvider>())));
        services.TryAddSingleton<IPushTokenProtector>(protector);
        services.TryAddSingleton(new MySqlDispatchLock(connectionString));
        services.TryAddSingleton<INotificationSubscriptionLock, NotificationSubscriptionLock>();
        services.AddDbContext<NotificationDbContext>(options => options.UseMySQL(connectionString)
            .EnableSensitiveDataLogging(false));
        services.AddScoped<IUnitOfWork, NotificationUnitOfWork>();
        services.AddScoped<INotificationRepository, NotificationRepository>();
        services.AddScoped<IAnnouncementRepository, AnnouncementRepository>();
        services.AddScoped<IPushSubscriptionRepository, PushSubscriptionRepository>();
        services.AddScoped<IProcessedNotificationEventRepository, ProcessedNotificationEventRepository>();
        services.AddScoped<NotificationDispatchRepository>();
        services.AddScoped<INotificationDispatchRepository>(provider => provider.GetRequiredService<NotificationDispatchRepository>());
        services.AddScoped<INotificationDeliveryAdministrationRepository>(provider => provider.GetRequiredService<NotificationDispatchRepository>());
        services.AddScoped<INotificationDispatchQueue>(provider => provider.GetRequiredService<NotificationDispatchRepository>());
        return services;
    }
}
