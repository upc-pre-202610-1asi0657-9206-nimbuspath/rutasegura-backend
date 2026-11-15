using Microsoft.EntityFrameworkCore;
using Monolith.Iam.Application.Internal.OutboundServices;
using Monolith.Iam.Domain.Model.Aggregates;
using Monolith.Iam.Domain.Model.ValueObjects;
using Monolith.Shared.Domain.Model.ValueObjects;
using Monolith.Shared.Infrastructure.Persistence.EFC.Configuration;

namespace Monolith.Iam.Infrastructure.Persistence.EFC.Seeding;

/// <summary>
///     Seeds the Iam bounded context with the initial organization and demo user accounts.
/// </summary>
/// <remarks>
///     Runs once on startup right after migrations: when the organizations table already has
///     rows the seeder is a no-op, so redeploys (e.g. Azure App Service container restarts)
///     never duplicate data. The identifiers are fixed so the frontend demo flows and other
///     seeded contexts can reference them deterministically.
/// </remarks>
public static class IamSeeder
{
    public static readonly Guid SeedOrganizationId = Guid.Parse("a0000000-0000-0000-0000-000000000001");

    public static async Task SeedAsync(AppDbContext context, IHashingService hashingService,
        CancellationToken cancellationToken = default)
    {
        if (!await context.Set<Organization>()
                .AnyAsync(organization => organization.Id == new OrganizationId(SeedOrganizationId), cancellationToken))
        {
            var organization = new Organization(new OrganizationName("Transportes Escolares Lima Norte S.A.C."));
            context.Entry(organization).Property(o => o.Id).CurrentValue = new OrganizationId(SeedOrganizationId);
            context.Add(organization);
        }

        await AddUserIfMissingAsync(context, hashingService, "b0000000-0000-0000-0000-000000000001",
            "Nickolas", "Quispe", "admin@saferoute.pe", "admin123", RoleTier.Admin, cancellationToken);
        await AddUserIfMissingAsync(context, hashingService, "b0000000-0000-0000-0000-000000000002",
            "Carlos", "Ramirez", "driver@saferoute.pe", "driver123", RoleTier.Driver, cancellationToken);
        await AddUserIfMissingAsync(context, hashingService, "b0000000-0000-0000-0000-000000000003",
            "Rosita", "Nery", "parent@saferoute.pe", "parent123", RoleTier.Parent, cancellationToken);
        await AddUserIfMissingAsync(context, hashingService, "b0000000-0000-0000-0000-000000000004",
            "Luis", "Torres", "luis.torres@saferoute.pe", "driver123", RoleTier.Driver, cancellationToken);
        await AddUserIfMissingAsync(context, hashingService, "b0000000-0000-0000-0000-000000000005",
            "Jorge", "Salas", "jorge.salas@gmail.com", "parent123", RoleTier.Parent, cancellationToken);
        await AddUserIfMissingAsync(context, hashingService, "b0000000-0000-0000-0000-000000000006",
            "Mariana", "Flores", "mariana.flores@gmail.com", "parent123", RoleTier.Parent, cancellationToken);
        await AddUserIfMissingAsync(context, hashingService, "b0000000-0000-0000-0000-000000000007",
            "Pablo", "Mendoza", "pablo.mendoza@gmail.com", "parent123", RoleTier.Parent, cancellationToken);
        await AddUserIfMissingAsync(context, hashingService, "b0000000-0000-0000-0000-000000000008",
            "Elena", "Castillo", "elena.castillo@gmail.com", "parent123", RoleTier.Parent, cancellationToken);
        await AddUserIfMissingAsync(context, hashingService, "b0000000-0000-0000-0000-000000000009",
            "Victor", "Rojas", "victor.rojas@gmail.com", "parent123", RoleTier.Parent, cancellationToken);

        await context.SaveChangesAsync(cancellationToken);
    }

    private static async Task AddUserIfMissingAsync(AppDbContext context, IHashingService hashingService, string id,
        string firstName, string lastName, string email, string password, string roleTier,
        CancellationToken cancellationToken)
    {
        var normalizedEmail = new Email(email, false);
        if (await context.Set<User>().AnyAsync(user => user.Email == normalizedEmail, cancellationToken)) return;

        var user = new User(
            new OrganizationId(SeedOrganizationId),
            new FullName(firstName, lastName),
            new Email(email),
            new PasswordHash(hashingService.HashPassword(password)),
            new RoleTier(roleTier));
        context.Entry(user).Property(u => u.Id).CurrentValue = new UserId(Guid.Parse(id));
        context.Add(user);
    }
}