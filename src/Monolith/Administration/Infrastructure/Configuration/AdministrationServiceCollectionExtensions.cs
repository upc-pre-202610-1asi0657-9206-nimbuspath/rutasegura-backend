using Microsoft.Extensions.DependencyInjection.Extensions;
using Monolith.Administration.Application.CommandServices;
using Monolith.Administration.Application.Internal.CommandServices;
using Monolith.Administration.Application.Internal.OutboundServices;
using Monolith.Administration.Application.Internal.OutboundServices.ACL;
using Monolith.Administration.Application.Internal.QueryServices;
using Monolith.Administration.Application.QueryServices;
using Monolith.Administration.Domain.Repositories;
using Monolith.Administration.Infrastructure.Acl;
using Monolith.Administration.Infrastructure.Messaging;
using Monolith.Administration.Infrastructure.Messaging.Inbound;
using Monolith.Administration.Infrastructure.Messaging.Outbox;
using Monolith.Administration.Infrastructure.Messaging.RabbitMq;
using Monolith.Administration.Infrastructure.Persistence.EFC.Repositories;
using Monolith.Administration.Infrastructure.Subscription;

namespace Monolith.Administration.Infrastructure.Configuration;

public static class AdministrationServiceCollectionExtensions
{
    /// <summary>
    ///     Registers the Administration bounded context: repositories, application services, outbound adapters
    ///     (plan limits, Iam ACL, outbox) and, when <c>Administration:Messaging:Enabled</c>, the RabbitMQ workers.
    /// </summary>
    public static IServiceCollection AddAdministrationBoundedContext(this IServiceCollection services,
        IConfiguration configuration)
    {
        services.TryAddSingleton(TimeProvider.System);

        // Repositories (Domain ports)
        services.AddScoped<IDriverRepository, DriverRepository>();
        services.AddScoped<IVehicleRepository, VehicleRepository>();
        services.AddScoped<IRouteRepository, RouteRepository>();
        services.AddScoped<IOperationalAlertRepository, OperationalAlertRepository>();
        services.AddScoped<ITripRecordRepository, TripRecordRepository>();

        // Application services
        services.AddScoped<IRosterCommandService, RosterCommandService>();
        services.AddScoped<IRoutePlanningCommandService, RoutePlanningCommandService>();
        services.AddScoped<IOperationsMonitoringCommandService, OperationsMonitoringCommandService>();
        services.AddScoped<IRosterQueryService, RosterQueryService>();
        services.AddScoped<IRoutePlanningQueryService, RoutePlanningQueryService>();
        services.AddScoped<IOperationsMonitoringQueryService, OperationsMonitoringQueryService>();

        // Outbound adapters (Application ports)
        services.Configure<PlanLimitsOptions>(configuration.GetSection(PlanLimitsOptions.Section));
        services.AddScoped<IPlanLimitsProvider, ConfiguredPlanLimitsProvider>();
        services.AddScoped<IIamAccountService, IamAccountService>();
        services.AddScoped<IIntegrationEventOutbox, EfIntegrationEventOutbox>();

        // Messaging with Trip & Tracking (RabbitMQ)
        var messagingSection = configuration.GetSection(AdministrationMessagingOptions.Section);
        services.Configure<AdministrationMessagingOptions>(messagingSection);
        services.AddScoped<InboundMessageProcessor>();
        if (messagingSection.GetValue<bool>(nameof(AdministrationMessagingOptions.Enabled)))
        {
            services.AddSingleton<RabbitMqConnection>();
            services.AddSingleton<IOutboxMessagePublisher, RabbitMqOutboxPublisher>();
            services.AddHostedService<OutboxPublisherService>();
            services.AddHostedService<TripTrackingEventsConsumer>();
        }

        return services;
    }
}
