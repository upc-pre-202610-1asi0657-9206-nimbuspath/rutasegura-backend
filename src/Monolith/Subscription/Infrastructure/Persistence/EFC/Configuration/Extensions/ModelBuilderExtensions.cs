using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Monolith.Shared.Domain.Model.ValueObjects;
using Monolith.Subscription.Domain.Model.Aggregates;
using Monolith.Subscription.Domain.Model.Entities;
using Monolith.Subscription.Domain.Model.ValueObjects;

namespace Monolith.Subscription.Infrastructure.Persistence.EFC.Configuration.Extensions;

/// <summary>
///     EF Core model of the Subscription bounded context (tables <c>subscription_*</c> after the snake_case convention,
///     as the report defines for the logical separation inside the monolith database).
/// </summary>
public static class ModelBuilderExtensions
{
    private static readonly ValueConverter<PlanCode, string> PlanCodeConverter =
        new(code => code.Value, value => new PlanCode(value));

    private static readonly ValueConverter<OrganizationId, Guid> OrganizationIdConverter =
        new(id => id.Identifier, value => new OrganizationId(value));

    private static readonly ValueConverter<PlanChangeStatus, string> StatusConverter =
        new(status => status.Value, value => new PlanChangeStatus(value));

    // Stored as UTC DATETIME so MySQL and SQLite (tests) can compare and order them.
    private static readonly ValueConverter<DateTimeOffset, DateTime> UtcDateTimeConverter = new(
        value => value.UtcDateTime,
        value => new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc)));

    private static readonly ValueConverter<DateTimeOffset?, DateTime?> NullableUtcDateTimeConverter = new(
        value => value.HasValue ? value.Value.UtcDateTime : null,
        value => value.HasValue ? new DateTimeOffset(DateTime.SpecifyKind(value.Value, DateTimeKind.Utc)) : null);

    public static void ApplySubscriptionConfiguration(this ModelBuilder builder)
    {
        builder.Entity<Plan>(plan =>
        {
            plan.ToTable("SubscriptionPlans");
            plan.HasKey(p => p.Code);
            plan.Property(p => p.Code).HasConversion(PlanCodeConverter).HasMaxLength(PlanCode.MaxLength);
            plan.Property(p => p.Name).HasMaxLength(Plan.NameMaxLength).IsRequired();
            plan.Property(p => p.Description).HasMaxLength(Plan.DescriptionMaxLength).IsRequired();
            plan.Property(p => p.MonthlyPriceInCents).IsRequired();
            plan.Property(p => p.Currency).HasMaxLength(3).IsRequired();
            plan.Ignore(p => p.MonthlyPrice);
            plan.Ignore(p => p.RequiresPayment);
            plan.HasData(PlanCatalog.Seed);
        });

        builder.Entity<TenantSubscription>(subscription =>
        {
            subscription.ToTable("SubscriptionTenants");
            subscription.HasKey(s => s.OrganizationId);
            subscription.Property(s => s.OrganizationId).HasConversion(OrganizationIdConverter).ValueGeneratedNever();
            subscription.Property(s => s.PlanCode).HasConversion(PlanCodeConverter).HasMaxLength(PlanCode.MaxLength)
                .IsRequired();
            subscription.Property(s => s.StartedAt).HasConversion(UtcDateTimeConverter);
            subscription.Property(s => s.PaidUntil).HasConversion(NullableUtcDateTimeConverter);
            subscription.Property(s => s.Version).IsConcurrencyToken();
            subscription.Ignore(s => s.PendingChange);
            subscription.HasMany(s => s.PlanChanges).WithOne().HasForeignKey("OrganizationId").IsRequired()
                .OnDelete(DeleteBehavior.Cascade);
            subscription.Navigation(s => s.PlanChanges).UsePropertyAccessMode(PropertyAccessMode.Field)
                .HasField("_planChanges");
        });

        builder.Entity<PlanChange>(change =>
        {
            change.ToTable("SubscriptionPlanChanges");
            change.HasKey(c => c.Id);
            change.Property(c => c.Id).ValueGeneratedNever();
            change.Property<OrganizationId>("OrganizationId").HasConversion(OrganizationIdConverter);
            change.Property(c => c.PreviousPlanCode).HasConversion(PlanCodeConverter)
                .HasMaxLength(PlanCode.MaxLength).IsRequired();
            change.Property(c => c.TargetPlanCode).HasConversion(PlanCodeConverter)
                .HasMaxLength(PlanCode.MaxLength).IsRequired();
            change.ComplexProperty(c => c.Price, price =>
            {
                price.IsRequired();
                price.Property(p => p.AmountInCents).HasColumnName("PriceInCents");
                price.Property(p => p.Currency).HasColumnName("PriceCurrency").HasMaxLength(3).IsRequired();
                price.Ignore(p => p.Amount);
                price.Ignore(p => p.IsZero);
            });
            change.Property(c => c.Status).HasConversion(StatusConverter).HasMaxLength(20).IsRequired();
            change.Property(c => c.PaymentReference).HasMaxLength(PlanChange.ReferenceMaxLength);
            change.Property(c => c.CheckoutUrl).HasMaxLength(PlanChange.CheckoutUrlMaxLength);
            change.Property(c => c.ResolutionDetail).HasMaxLength(PlanChange.DetailMaxLength);
            change.Property(c => c.RequestedAt).HasConversion(UtcDateTimeConverter);
            change.Property(c => c.ResolvedAt).HasConversion(NullableUtcDateTimeConverter);
            // A checkout belongs to exactly one plan change: notifications are matched by it.
            change.HasIndex(c => c.PaymentReference).IsUnique();
        });

        builder.Entity<ProcessedPaymentEvent>(paymentEvent =>
        {
            paymentEvent.ToTable("SubscriptionPaymentEvents");
            // The event id of the gateway is the key: a retried notification cannot be recorded twice.
            paymentEvent.HasKey(e => e.EventId);
            paymentEvent.Property(e => e.EventId).HasMaxLength(ProcessedPaymentEvent.EventIdMaxLength);
            paymentEvent.Property(e => e.PaymentReference).HasMaxLength(ProcessedPaymentEvent.ReferenceMaxLength)
                .IsRequired();
            paymentEvent.Property(e => e.Outcome).HasMaxLength(ProcessedPaymentEvent.OutcomeMaxLength).IsRequired();
            paymentEvent.Property(e => e.Resolution).HasMaxLength(ProcessedPaymentEvent.ResolutionMaxLength)
                .IsRequired();
            paymentEvent.Property(e => e.ReceivedAt).HasConversion(UtcDateTimeConverter);
            paymentEvent.HasIndex(e => e.PaymentReference);
        });
    }
}
