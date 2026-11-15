using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Monolith.Shared.Infrastructure.Persistence.EFC.Configuration;
using Monolith.Shared.Domain.Model.ValueObjects;
using Monolith.Administration.Domain.Model.ValueObjects;
using Monolith.Stakeholder.Domain.Model.Aggregates;
using Route = Monolith.Administration.Domain.Model.Aggregates.Route;
using Direction = Monolith.Administration.Domain.Model.ValueObjects.RouteDirection;
namespace RutaSegura.Monolith.Administration.IntegrationTests;
public sealed class ContractMySqlFactAttribute : FactAttribute
{
    public ContractMySqlFactAttribute([CallerFilePath] string? path = null, [CallerLineNumber] int line = 0) : base(path, line)
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("RUTASEGURA_CONTRACT_MYSQL"))) Skip = "Requires an isolated temporary MySQL database in RUTASEGURA_CONTRACT_MYSQL.";
    }
}
public sealed class GuardianMigrationMySqlTests
{
    [ContractMySqlFact]
    public async Task UpgradePreservesExistingStudentsAsPendingAndRejectsStaleRoadGeometry()
    {
        var ct = TestContext.Current.CancellationToken;
        var connection = Environment.GetEnvironmentVariable("RUTASEGURA_CONTRACT_MYSQL")!;
        var target = new MySql.Data.MySqlClient.MySqlConnectionStringBuilder(connection);
        if (target.Server is not ("127.0.0.1" or "localhost") || !target.Database.StartsWith("rutasegura_contract_test_", StringComparison.Ordinal))
            throw new InvalidOperationException("This test only accepts a localhost database named rutasegura_contract_test_<unique-id>.");
        var options = new DbContextOptionsBuilder<AppDbContext>().UseMySQL(connection).Options;
        await using var db = new AppDbContext(options);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20261025233340_AddStakeholderBoundedContext", ct);
        var student = Guid.NewGuid().ToString(); var guardian = Guid.NewGuid().ToString(); var org = Guid.NewGuid().ToString(); var enrollment = Guid.NewGuid().ToString(); var legacyRoute = Guid.NewGuid().ToString();
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO stakeholder_students (id, guardian_user_id, status, first_name, last_name, pickup_address, pickup_latitude, pickup_longitude) VALUES ({student}, {guardian}, 'ACTIVE', 'Alumno', 'Prueba', 'Surco', -12.11, -76.98)", ct);
        // Historical enrollments must remain reviewable, without granting access automatically.
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO stakeholder_route_enrollments (id, route_id, organization_id, route_name, direction, status, enrolled_at, student_id) VALUES ({enrollment}, {legacyRoute}, {org}, 'Ruta anterior', 'PICKUP', 'CANCELLED', UTC_TIMESTAMP(6), {student})", ct);
        await migrator.MigrateAsync(cancellationToken: ct);
        db.Database.HasPendingModelChanges().ShouldBeFalse();
        var pending = await db.Set<GuardianAccessGrant>().SingleAsync(ct);
        pending.Status.ShouldBe("PENDING"); pending.ReviewedBy.ShouldBeNull(); pending.StudentId.ShouldBe(Guid.Parse(student));
        var route = new Route(new OrganizationId(Guid.Parse(org)), new RouteName("Prueba geometría"), Direction.CreatePickup(), new RouteSchedule(new TimeOnly(6, 0), new TimeOnly(7, 0), "America/Lima", [DayOfWeek.Monday]));
        route.AddStop("Uno", null, new(-12.11, -76.98)); route.AddStop("Dos", null, new(-12.12, -76.99));
        db.Add(route); await db.SaveChangesAsync(ct);
        await using var stale = new AppDbContext(options);
        var staleRoute = await stale.Set<Route>().SingleAsync(r => r.Id == route.Id, ct);
        route.AddStop("Tres", null, new(-12.13, -77.0)); await db.SaveChangesAsync(ct);
        staleRoute.SetItinerary(new RouteItinerary("{\"type\":\"LineString\",\"coordinates\":[[-76.98,-12.11],[-76.99,-12.12]]}", 1000, 200, DateTimeOffset.UtcNow));
        await Should.ThrowAsync<DbUpdateConcurrencyException>(() => stale.SaveChangesAsync(ct));
    }
}
