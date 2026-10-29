namespace Monolith.Subscription.Domain.Model.Exceptions;

/// <summary>A business rule of the Subscription context was violated.</summary>
public class SubscriptionDomainException(SubscriptionError error, string message) : Exception(message)
{
    public SubscriptionError Error { get; } = error;
}
