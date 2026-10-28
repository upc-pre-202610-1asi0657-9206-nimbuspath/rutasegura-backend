namespace Monolith.Subscription.Infrastructure.Persistence.EFC.Configuration;

/// <summary>
///     Initial plan catalog, seeded by the migration. BASIC keeps the limit the monolith used before Subscription
///     existed (3 active routes). The names follow the report (US-07 "Plan Completo", impact map "Premium"); the
///     prices and the limits of the paid plans are proposals until the team confirms them — change them here and add
///     a migration.
/// </summary>
public static class PlanCatalog
{
    public const string Basic = "BASIC";
    public const string Complete = "COMPLETE";
    public const string Premium = "PREMIUM";

    internal static readonly object[] Seed =
    [
        Row(Basic, "Plan Básico", "Para conductores independientes que empiezan: hasta 3 rutas activas y 3 vehículos.",
            0, 3, 3, isDefault: true),
        Row(Complete, "Plan Completo", "Para pequeñas empresas de transporte: hasta 10 rutas activas y 10 vehículos.",
            9_990, 10, 10, isDefault: false),
        Row(Premium, "Plan Premium", "Para operaciones grandes: hasta 30 rutas activas y 30 vehículos.",
            19_990, 30, 30, isDefault: false)
    ];

    private static object Row(string code, string name, string description, long monthlyPriceInCents,
        int maxActiveRoutes, int maxVehicles, bool isDefault) => new
    {
        Code = new Domain.Model.ValueObjects.PlanCode(code),
        Name = name,
        Description = description,
        MonthlyPriceInCents = monthlyPriceInCents,
        Currency = Domain.Model.ValueObjects.Money.DefaultCurrency,
        MaxActiveRoutes = maxActiveRoutes,
        MaxVehicles = maxVehicles,
        IsDefault = isDefault,
        IsAvailable = true
    };
}
