using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Monolith.Subscription.Application.CommandServices;
using Monolith.Subscription.Application.Internal.CommandServices;
using Monolith.Subscription.Application.Internal.OutboundServices;
using Monolith.Subscription.Application.Internal.QueryServices;
using Monolith.Subscription.Application.QueryServices;
using Monolith.Subscription.Domain.Repositories;
using Monolith.Subscription.Infrastructure.Payments;
using Monolith.Subscription.Infrastructure.Payments.Sandbox;
using Monolith.Subscription.Infrastructure.Persistence.EFC.Repositories;

namespace Monolith.Subscription.Infrastructure.Configuration;

public static class SubscriptionServiceCollectionExtensions
{
    /// <summary>
    ///     Registers the Subscription bounded context: plan catalog, contracts, payment notifications and the adapter of
    ///     the payment gateway selected in <c>Subscription:PaymentGateway:Provider</c>.
    /// </summary>
    public static IServiceCollection AddSubscriptionBoundedContext(this IServiceCollection services,
        IConfiguration configuration)
    {
        services.TryAddSingleton(TimeProvider.System);

        services.AddScoped<IPlanRepository, PlanRepository>();
        services.AddScoped<ITenantSubscriptionRepository, TenantSubscriptionRepository>();
        services.AddScoped<IProcessedPaymentEventRepository, ProcessedPaymentEventRepository>();
        services.AddScoped<ISubscriptionUnitOfWork, SubscriptionUnitOfWork>();

        services.AddScoped<ISubscriptionCommandService, SubscriptionCommandService>();
        services.AddScoped<ISubscriptionQueryService, SubscriptionQueryService>();

        services.AddOptions<PaymentGatewayOptions>().Bind(configuration.GetSection(PaymentGatewayOptions.Section));
        var provider = configuration[$"{PaymentGatewayOptions.Section}:Provider"] ?? PaymentGatewayOptions.SandboxProvider;
        if (!string.Equals(provider, PaymentGatewayOptions.SandboxProvider, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"Payment gateway '{provider}' is not implemented. Supported: {PaymentGatewayOptions.SandboxProvider}.");

        // One instance: the secret generated when none is configured must sign and verify the same notifications.
        services.AddSingleton<SandboxPaymentGateway>();
        services.AddSingleton<IPaymentGateway>(sp => sp.GetRequiredService<SandboxPaymentGateway>());
        services.AddSingleton<ISimulatedPaymentNotifier>(sp => sp.GetRequiredService<SandboxPaymentGateway>());
        return services;
    }
}
