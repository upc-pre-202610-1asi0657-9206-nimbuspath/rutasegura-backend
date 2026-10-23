using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using RutaSegura.BuildingBlocks.Application.Persistence;
using RutaSegura.BuildingBlocks.Messaging;
using RutaSegura.TripTracking.Application.Abstractions;
using RutaSegura.TripTracking.Infrastructure.Health;
using RutaSegura.TripTracking.Infrastructure.LiveUpdates;
using RutaSegura.TripTracking.Infrastructure.Messaging.Inbound;
using RutaSegura.TripTracking.Infrastructure.Messaging.Outbox;
using RutaSegura.TripTracking.Infrastructure.Messaging.RabbitMq;
using RutaSegura.TripTracking.Infrastructure.Options;
using RutaSegura.TripTracking.Infrastructure.Persistence;
using RutaSegura.TripTracking.Infrastructure.Persistence.Repositories;
using RutaSegura.TripTracking.Infrastructure.Scheduling;
using RutaSegura.TripTracking.Infrastructure.Seeding;

namespace RutaSegura.TripTracking.Infrastructure.DependencyInjection;

public static class TripTrackingInfrastructureExtensions
{
    public const string ReadinessTag = "ready";

    /// <summary>
    /// Registra los adaptadores de salida del hexágono (puertos de Application) y los workers.
    /// <list type="bullet">
    /// <item><c>ConnectionStrings:TripTracking</c> (MySQL) — obligatorio.</item>
    /// <item><c>ConnectionStrings:Redis</c> — opcional; sin él la última posición vive en memoria del proceso.</item>
    /// <item><c>RabbitMq:Enabled</c> — sin broker el Outbox acumula y el canal en vivo es en proceso.</item>
    /// </list>
    /// </summary>
    public static IServiceCollection AddTripTrackingInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddSingleton(TimeProvider.System);

        services.AddOptions<PersistenceOptions>().Bind(configuration.GetSection(PersistenceOptions.Section));
        services.AddOptions<RabbitMqOptions>().Bind(configuration.GetSection(RabbitMqOptions.Section)).ValidateDataAnnotations().ValidateOnStart();
        services.AddOptions<OutboxOptions>().Bind(configuration.GetSection(OutboxOptions.Section)).ValidateDataAnnotations().ValidateOnStart();
        services.AddOptions<LivePositionOptions>().Bind(configuration.GetSection(LivePositionOptions.Section));
        services.AddOptions<SchedulingOptions>().Bind(configuration.GetSection(SchedulingOptions.Section)).ValidateDataAnnotations().ValidateOnStart();
        services.AddOptions<DemoSeedOptions>().Bind(configuration.GetSection(DemoSeedOptions.Section));

        AddPersistence(services, configuration);
        AddCaching(services, configuration);
        var messagingEnabled = configuration.GetValue<bool>($"{RabbitMqOptions.Section}:{nameof(RabbitMqOptions.Enabled)}");
        AddMessaging(services, messagingEnabled);

        if (configuration.GetValue($"{SchedulingOptions.Section}:{nameof(SchedulingOptions.Enabled)}", true))
            services.AddHostedService<TripSchedulingWorker>();
        if (configuration.GetValue<bool>($"{DemoSeedOptions.Section}:{nameof(DemoSeedOptions.Demo)}"))
            services.AddHostedService<DemoDataSeeder>();

        var health = services.AddHealthChecks()
            .AddCheck<MySqlHealthCheck>("mysql", HealthStatus.Unhealthy, [ReadinessTag])
            .AddCheck<CacheHealthCheck>("cache", HealthStatus.Degraded, [ReadinessTag]);
        if (messagingEnabled)
            health.AddCheck<RabbitMqHealthCheck>("rabbitmq", HealthStatus.Unhealthy, [ReadinessTag]);

        return services;
    }

    private static void AddPersistence(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(PersistenceOptions.ConnectionStringName)
            ?? throw new InvalidOperationException(
                $"Falta ConnectionStrings:{PersistenceOptions.ConnectionStringName} (MySQL de Trip & Tracking).");

        services.AddDbContext<TripTrackingDbContext>(options => options
            .UseMySQL(connectionString, mysql => mysql.MigrationsHistoryTable("__ef_migrations_history")));

        services.AddScoped<PostCommitActions>();
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        services.AddScoped<ITripRepository, TripRepository>();
        services.AddScoped<ITrackPointRepository, TrackPointRepository>();
        services.AddScoped<RouteCatalog>();
        services.AddScoped<IRouteCatalog>(sp => sp.GetRequiredService<RouteCatalog>());
        services.AddScoped<IRouteCatalogStore>(sp => sp.GetRequiredService<RouteCatalog>());
        services.AddScoped<GuardianDirectory>();
        services.AddScoped<IGuardianDirectory>(sp => sp.GetRequiredService<GuardianDirectory>());
        services.AddScoped<IGuardianDirectoryStore>(sp => sp.GetRequiredService<GuardianDirectory>());
    }

    private static void AddCaching(IServiceCollection services, IConfiguration configuration)
    {
        var redis = configuration.GetConnectionString("Redis");
        if (string.IsNullOrWhiteSpace(redis))
        {
            services.AddDistributedMemoryCache();
        }
        else
        {
            services.AddStackExchangeRedisCache(options =>
            {
                options.Configuration = redis;
                options.InstanceName = "rutasegura:";
            });
        }

        services.AddSingleton<ILastPositionCache, DistributedLastPositionCache>();
    }

    private static void AddMessaging(IServiceCollection services, bool enabled)
    {
        services.TryAddSingleton<ILiveUpdateSink, NullLiveUpdateSink>();
        services.AddScoped<ILivePositionPublisher, LivePositionPublisher>();

        if (!enabled)
        {
            services.AddSingleton<ILiveUpdateBroadcaster, InProcessLiveUpdateBroadcaster>();
            return;
        }

        services.AddSingleton<RabbitMqConnection>();
        services.AddSingleton<RabbitMqMessagePublisher>();
        services.AddSingleton<IMessagePublisher>(sp => sp.GetRequiredService<RabbitMqMessagePublisher>());
        services.AddSingleton<ILiveUpdateBroadcaster, RabbitMqLiveUpdateBroadcaster>();
        services.AddHostedService<OutboxProcessor>();
        services.AddHostedService<InboundEventsConsumer>();
        services.AddHostedService<LiveUpdatesRelay>();
    }
}
