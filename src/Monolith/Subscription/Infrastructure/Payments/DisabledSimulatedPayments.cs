using Monolith.Subscription.Application.Internal.OutboundServices;

namespace Monolith.Subscription.Infrastructure.Payments;

/// <summary>A real gateway is configured: its checkouts can only be paid on the page of the provider.</summary>
public sealed class DisabledSimulatedPayments : ISimulatedPaymentNotifier
{
    public bool Enabled => false;

    public SignedPaymentNotification Create(string paymentReference, bool approved, long amountInCents,
        string currency) =>
        throw new InvalidOperationException("Simulated payments are only available with the sandbox gateway.");
}
