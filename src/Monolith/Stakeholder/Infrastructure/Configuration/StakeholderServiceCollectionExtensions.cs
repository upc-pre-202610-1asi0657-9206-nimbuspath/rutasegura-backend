using Microsoft.Extensions.DependencyInjection.Extensions;
using Monolith.Stakeholder.Application.CommandServices;
using Monolith.Stakeholder.Application.Internal.CommandServices;
using Monolith.Stakeholder.Application.Internal.OutboundServices;
using Monolith.Stakeholder.Application.Internal.OutboundServices.ACL;
using Monolith.Stakeholder.Application.Internal.QueryServices;
using Monolith.Stakeholder.Application.QueryServices;
using Monolith.Stakeholder.Domain.Repositories;
using Monolith.Stakeholder.Infrastructure.Acl;
using Monolith.Stakeholder.Infrastructure.Messaging;
using Monolith.Stakeholder.Infrastructure.Persistence.EFC.Repositories;

namespace Monolith.Stakeholder.Infrastructure.Configuration;

public static class StakeholderServiceCollectionExtensions
{
    /// <summary>
    ///     Registers the Stakeholder bounded context: repositories, application services, the ACLs towards Route
    ///     Planning and Iam, the outbox and — when messaging is enabled — the publisher of <c>stakeholder.events</c>.
    /// </summary>
    /// <remarks>
    ///     The broker connection and the <c>Enabled</c> flag default to <c>Administration:Messaging</c> (one broker per
    ///     monolith); <c>Stakeholder:Messaging</c> overrides any of them.
    /// </remarks>
    public static IServiceCollection AddStakeholderBoundedContext(this IServiceCollection services,
        IConfiguration configuration)
    {
        services.TryAddSingleton(TimeProvider.System);

        services.AddScoped<IStudentRepository, StudentRepository>();
        services.AddScoped<IGuardianProfileRepository, GuardianProfileRepository>();
        services.AddScoped<IStudentPhotoStore, EfStudentPhotoStore>();

        services.AddScoped<IStakeholderCommandService, StakeholderCommandService>();
        services.AddScoped<IStakeholderQueryService, StakeholderQueryService>();

        services.AddScoped<IRoutePlanningService, RoutePlanningService>();
        services.AddScoped<IGuardianDirectory, GuardianDirectory>();
        services.AddScoped<IStakeholderEventOutbox, EfStakeholderEventOutbox>();

        var shared = configuration.GetSection("Administration:Messaging");
        var own = configuration.GetSection(StakeholderMessagingOptions.Section);
        services.Configure<StakeholderMessagingOptions>(options =>
        {
            options.HostName = shared["HostName"] ?? options.HostName;
            options.Port = shared.GetValue("Port", options.Port);
            options.UserName = shared["UserName"] ?? options.UserName;
            options.Password = shared["Password"] ?? options.Password;
            options.VirtualHost = shared["VirtualHost"] ?? options.VirtualHost;
            options.Enabled = shared.GetValue("Enabled", false);
            own.Bind(options);
        });

        var enabled = own.GetValue<bool?>(nameof(StakeholderMessagingOptions.Enabled)) ??
                      shared.GetValue("Enabled", false);
        if (enabled)
        {
            services.AddSingleton<IStakeholderMessagePublisher, RabbitMqStakeholderPublisher>();
            services.AddHostedService<StakeholderOutboxPublisherService>();
        }

        return services;
    }
}
