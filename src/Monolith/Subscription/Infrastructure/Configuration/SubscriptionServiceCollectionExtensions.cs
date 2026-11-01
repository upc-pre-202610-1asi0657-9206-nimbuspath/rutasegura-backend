using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Monolith.Subscription.Application.CommandServices;
using Monolith.Subscription.Application.Internal.CommandServices;
using Monolith.Subscription.Application.Internal.OutboundServices;
using Monolith.Subscription.Application.Internal.QueryServices;
using Monolith.Subscription.Application.QueryServices;
using Monolith.Subscription.Domain.Repositories;
using Monolith.Subscription.Infrastructure.Payments;
using Monolith.Subscription.Infrastructure.Payments.MercadoPago;
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
        var options = configuration.GetSection(PaymentGatewayOptions.Section).Get<PaymentGatewayOptions>() ?? new();
        if (string.Equals(options.Provider, PaymentGatewayOptions.MercadoPagoProvider,
                StringComparison.OrdinalIgnoreCase))
            AddMercadoPago(services, options);
        else if (string.Equals(options.Provider, PaymentGatewayOptions.SandboxProvider,
                     StringComparison.OrdinalIgnoreCase))
            AddSandbox(services);
        else
            throw new InvalidOperationException(
                $"Payment gateway '{options.Provider}' is not implemented. Supported: " +
                $"{PaymentGatewayOptions.MercadoPagoProvider}, {PaymentGatewayOptions.SandboxProvider}.");
        return services;
    }

    private static void AddSandbox(IServiceCollection services)
    {
        // One instance: the secret generated when none is configured must sign and verify the same notifications.
        services.AddSingleton<SandboxPaymentGateway>();
        services.AddSingleton<IPaymentGateway>(sp => sp.GetRequiredService<SandboxPaymentGateway>());
        services.AddSingleton<ISimulatedPaymentNotifier>(sp => sp.GetRequiredService<SandboxPaymentGateway>());
    }

    private static void AddMercadoPago(IServiceCollection services, PaymentGatewayOptions options)
    {
        // Fail at startup rather than on the first payment; the values themselves are never logged.
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(options.MercadoPago.AccessToken)) missing.Add("MercadoPago:AccessToken");
        if (string.IsNullOrWhiteSpace(options.WebhookSecret)) missing.Add("WebhookSecret");
        if (!Uri.TryCreate(options.MercadoPago.NotificationUrl, UriKind.Absolute, out var notificationUrl) ||
            notificationUrl.Scheme != Uri.UriSchemeHttps)
            missing.Add("MercadoPago:NotificationUrl (public https URL)");
        if (missing.Count > 0)
            throw new InvalidOperationException(
                $"Mercado Pago needs {string.Join(", ", missing)} in {PaymentGatewayOptions.Section}. " +
                "Set them through environment variables or user-secrets, never in appsettings.");

        services.AddHttpClient<IPaymentGateway, MercadoPagoPaymentGateway>(client =>
        {
            client.BaseAddress = new Uri(options.MercadoPago.ApiBaseUrl);
            client.Timeout = TimeSpan.FromSeconds(options.MercadoPago.TimeoutSeconds);
        });
        services.AddSingleton<ISimulatedPaymentNotifier, DisabledSimulatedPayments>();
    }
}
