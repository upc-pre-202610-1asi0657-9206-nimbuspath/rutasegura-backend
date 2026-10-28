using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Monolith.Shared.Domain.Model.ValueObjects;
using Monolith.Subscription.Application.Internal.CommandServices;
using Monolith.Subscription.Application.Internal.OutboundServices;
using Monolith.Subscription.Application.Internal.QueryServices;
using Monolith.Subscription.Domain.Model.Aggregates;
using Monolith.Subscription.Domain.Model.ValueObjects;
using Monolith.Subscription.Domain.Repositories;
using Monolith.Subscription.Infrastructure.Payments;
using Monolith.Subscription.Infrastructure.Payments.Sandbox;

namespace RutaSegura.Monolith.Subscription.Tests.Support;

/// <summary>Object Mother of the Subscription tests: the catalog of the migration and a fixed clock.</summary>
public static class SubscriptionMother
{
    public static readonly DateTimeOffset Now = new(2026, 10, 28, 13, 0, 0, TimeSpan.Zero);
    public static readonly Guid Organization = Guid.Parse("a0000000-0000-0000-0000-0000000000aa");
    public static readonly Guid Administrator = Guid.Parse("b0000000-0000-0000-0000-0000000000bb");
    public const string WebhookSecret = "unit-tests-webhook-secret-0123456789";

    public static Plan Basic() => new(new PlanCode("BASIC"), "Plan Básico", "Gratis", new Money(0, "PEN"), 3, 3,
        isDefault: true);

    public static Plan Complete() => new(new PlanCode("COMPLETE"), "Plan Completo", "Pequeñas empresas",
        new Money(9_990, "PEN"), 10, 10, isDefault: false);

    public static Plan Premium() => new(new PlanCode("PREMIUM"), "Plan Premium", "Operaciones grandes",
        new Money(19_990, "PEN"), 30, 30, isDefault: false);

    public static Plan Withdrawn() => new(new PlanCode("LEGACY"), "Plan Antiguo", "Ya no se ofrece",
        new Money(4_990, "PEN"), 5, 5, isDefault: false, isAvailable: false);

    public static TenantSubscription NewSubscription() =>
        new(new OrganizationId(Organization), Basic(), Now);

    public static SandboxPaymentGateway Sandbox(bool allowSimulatedPayments = true, string? secret = WebhookSecret) =>
        new(Options.Create(new PaymentGatewayOptions
        {
            WebhookSecret = secret ?? string.Empty,
            AllowSimulatedPayments = allowSimulatedPayments,
            CheckoutBaseUrl = "https://rutasegura.test/checkout"
        }));
}

public sealed class InMemoryPlans : IPlanRepository
{
    public List<Plan> Plans { get; } =
        [SubscriptionMother.Basic(), SubscriptionMother.Complete(), SubscriptionMother.Premium(),
            SubscriptionMother.Withdrawn()];

    public Task<Plan?> FindByCodeAsync(PlanCode code, CancellationToken cancellationToken) =>
        Task.FromResult(Plans.SingleOrDefault(p => p.Code == code));

    public Task<Plan> GetDefaultAsync(CancellationToken cancellationToken) =>
        Task.FromResult(Plans.Single(p => p.IsDefault));

    public Task<IReadOnlyList<Plan>> ListAvailableAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Plan>>(Plans.Where(p => p.IsAvailable).OrderBy(p => p.MonthlyPriceInCents)
            .ToList());
}

public sealed class InMemorySubscriptions : ITenantSubscriptionRepository
{
    public List<TenantSubscription> Saved { get; } = [];

    public Task<TenantSubscription?> FindByOrganizationAsync(OrganizationId organizationId,
        CancellationToken cancellationToken) =>
        Task.FromResult(Saved.SingleOrDefault(s => s.OrganizationId == organizationId));

    public Task<TenantSubscription?> FindByPaymentReferenceAsync(string paymentReference,
        CancellationToken cancellationToken) =>
        Task.FromResult(Saved.SingleOrDefault(s => s.PlanChanges.Any(c => c.PaymentReference == paymentReference)));

    public Task AddAsync(TenantSubscription subscription, CancellationToken cancellationToken)
    {
        Saved.Add(subscription);
        return Task.CompletedTask;
    }

    public void Update(TenantSubscription subscription)
    {
    }
}

public sealed class InMemoryPaymentEvents : IProcessedPaymentEventRepository
{
    public List<ProcessedPaymentEvent> Saved { get; } = [];

    public Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken) =>
        Task.FromResult(Saved.Any(e => e.EventId == eventId));

    public Task AddAsync(ProcessedPaymentEvent paymentEvent, CancellationToken cancellationToken)
    {
        Saved.Add(paymentEvent);
        return Task.CompletedTask;
    }
}

public sealed class FakeUnitOfWork : ISubscriptionUnitOfWork
{
    public int Saves { get; private set; }

    /// <summary>Results of the next saves (true by default); false simulates a lost concurrent save.</summary>
    public Queue<bool> NextResults { get; } = new();

    public Task<bool> TryCompleteAsync(CancellationToken cancellationToken)
    {
        Saves++;
        return Task.FromResult(NextResults.Count == 0 || NextResults.Dequeue());
    }
}

/// <summary>Gateway double: the sandbox for notifications, with a switchable checkout to simulate an outage.</summary>
public sealed class FakeGateway(SandboxPaymentGateway sandbox) : IPaymentGateway
{
    public bool Down { get; set; }

    public List<PaymentCheckoutRequest> Requests { get; } = [];

    public string Provider => "Fake";

    public string SignatureHeader => SandboxPaymentGateway.SignatureHeader;

    public Task<PaymentCheckout> CreateCheckoutAsync(PaymentCheckoutRequest request,
        CancellationToken cancellationToken)
    {
        Requests.Add(request);
        if (Down) throw new PaymentGatewayUnavailableException("timeout");
        return ((IPaymentGateway)sandbox).CreateCheckoutAsync(request, cancellationToken);
    }

    public PaymentNotification? ReadNotification(string payload, string? signature) =>
        ((IPaymentGateway)sandbox).ReadNotification(payload, signature);
}

/// <summary>Command and query services wired with the in-memory doubles.</summary>
public sealed class SubscriptionFixture
{
    public SubscriptionFixture(bool allowSimulatedPayments = true)
    {
        Sandbox = SubscriptionMother.Sandbox(allowSimulatedPayments);
        Gateway = new FakeGateway(Sandbox);
        Commands = new SubscriptionCommandService(Plans, Subscriptions, PaymentEvents, Gateway, Sandbox, UnitOfWork,
            Clock);
        Queries = new SubscriptionQueryService(Plans, Subscriptions, Clock);
    }

    public FakeTimeProvider Clock { get; } = new(SubscriptionMother.Now);
    public InMemoryPlans Plans { get; } = new();
    public InMemorySubscriptions Subscriptions { get; } = new();
    public InMemoryPaymentEvents PaymentEvents { get; } = new();
    public FakeUnitOfWork UnitOfWork { get; } = new();
    public SandboxPaymentGateway Sandbox { get; }
    public FakeGateway Gateway { get; }
    public SubscriptionCommandService Commands { get; }
    public SubscriptionQueryService Queries { get; }
}
