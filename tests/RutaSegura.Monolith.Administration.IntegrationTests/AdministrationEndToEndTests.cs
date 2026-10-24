using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Monolith.Administration.Infrastructure.Messaging.Outbox;
using RutaSegura.Monolith.Administration.IntegrationTests.Support;

namespace RutaSegura.Monolith.Administration.IntegrationTests;

/// <summary>
///     All the layers together: REST (Interfaces) → use cases (Application) → aggregates (Domain) → EF Core +
///     outbox (Infrastructure), with a real Iam token of the seeded administrator.
/// </summary>
public sealed class AdministrationEndToEndTests : IDisposable
{
    private readonly MonolithApp _app = new(maxActiveRoutes: 2);

    public void Dispose() => _app.Dispose();

    [Fact]
    public async Task FullFlow_RegisterVerifyPlanAssignActivate_PersistsTheRouteAndPublishesItForTripTracking()
    {
        var admin = await _app.AdminAsync();

        // Roster
        var driver = await admin.PostJson("/api/v1/roster/drivers", new
        {
            userId = SeedAccounts.Driver,
            firstName = "Carlos",
            lastName = "Ramirez",
            documentNumber = "45678912",
            licenseNumber = "Q45678912",
            licenseCategory = "A-IIb",
            licenseExpiresOn = "2030-12-31",
            phoneNumber = "987 654 321"
        }).ExpectAsync(HttpStatusCode.Created);
        driver.GetProperty("status").GetString().ShouldBe("PENDING_VERIFICATION");
        (await admin.PostJson($"/api/v1/roster/drivers/{driver.Id()}/verify").ExpectAsync(HttpStatusCode.OK))
            .GetProperty("status").GetString().ShouldBe("VERIFIED");
        var vehicleId = await admin.RegisterVehicleAsync("abc-123", capacity: 15);

        // Route Planning
        var routeId = await admin.CreateRouteAsync();
        var route = await admin.AddTwoStopsAsync(routeId);
        var mateo = Guid.NewGuid();
        var lucia = Guid.NewGuid();
        await admin.PutJson($"/api/v1/routes/{routeId}/stops/{route.StopId(0)}/students/{mateo}")
            .ExpectAsync(HttpStatusCode.OK);
        await admin.PutJson($"/api/v1/routes/{routeId}/stops/{route.StopId(1)}/students/{lucia}")
            .ExpectAsync(HttpStatusCode.OK);
        await admin.PutJson($"/api/v1/routes/{routeId}/assignment", new { driverId = driver.Id(), vehicleId })
            .ExpectAsync(HttpStatusCode.OK);
        var activated = await admin.PostJson($"/api/v1/routes/{routeId}/activate").ExpectAsync(HttpStatusCode.OK);
        activated.GetProperty("status").GetString().ShouldBe("ACTIVE");

        // Reloaded from the database by a new request
        var reloaded = await admin.Get($"/api/v1/routes/{routeId}").ExpectAsync(HttpStatusCode.OK);
        reloaded.GetProperty("stopPurpose").GetString().ShouldBe("PICK_UP");
        reloaded.GetProperty("studentCount").GetInt32().ShouldBe(2);
        var schedule = reloaded.GetProperty("schedule");
        schedule.GetProperty("departureTime").GetString().ShouldBe("06:45:00");
        schedule.GetProperty("timeZoneId").GetString().ShouldBe("America/Lima");
        schedule.GetProperty("operatingDays").GetArrayLength().ShouldBe(5);
        reloaded.GetProperty("assignment").GetProperty("driverId").GetGuid().ShouldBe(driver.Id());
        var stops = reloaded.GetProperty("stops");
        stops[0].GetProperty("sequence").GetInt32().ShouldBe(1);
        stops[0].GetProperty("studentIds")[0].GetGuid().ShouldBe(mateo);
        stops[1].GetProperty("latitude").GetDouble().ShouldBe(-12.10890);

        // Outbox: what Trip & Tracking will receive, committed with the change
        var outbox = await _app.WithDbAsync(db => db.Set<OutboxMessage>().OrderBy(m => m.CreatedAt).ToListAsync());
        outbox.Select(m => m.MessageType)
            .ShouldBe(["administration.route-assigned", "administration.route-published"], ignoreOrder: false);
        var published = JsonDocument.Parse(outbox[1].Payload).RootElement;
        published.GetProperty("routeId").GetGuid().ShouldBe(routeId);
        published.GetProperty("organizationId").GetGuid().ShouldBe(SeedAccounts.Organization);
        published.GetProperty("driverId").GetGuid().ShouldBe(SeedAccounts.Driver); // Iam account, not the roster id
        published.GetProperty("vehicleId").GetGuid().ShouldBe(vehicleId);
        published.GetProperty("direction").GetString().ShouldBe("Pickup");
        published.GetProperty("departureTime").GetString().ShouldBe("06:45");
        published.GetProperty("students").GetArrayLength().ShouldBe(2);

        // Plan quota
        var quota = await admin.Get("/api/v1/routes/quota").ExpectAsync(HttpStatusCode.OK);
        quota.GetProperty("activeRoutes").GetInt32().ShouldBe(1);
        quota.GetProperty("maxActiveRoutes").GetInt32().ShouldBe(2);
    }

    [Fact]
    public async Task ActivateRoute_BeyondThePlanLimit_Returns409WithPlanRouteLimitReached()
    {
        var admin = await _app.AdminAsync();
        var driverId = await admin.RegisterVerifiedDriverAsync(SeedAccounts.Driver);
        var vehicleId = await admin.RegisterVehicleAsync();
        var first = await admin.ReadyRouteAsync(driverId, vehicleId, "Ruta 1", "06:00:00", "06:40:00");
        var second = await admin.ReadyRouteAsync(driverId, vehicleId, "Ruta 2", "07:00:00", "07:40:00");
        var third = await admin.ReadyRouteAsync(driverId, vehicleId, "Ruta 3", "08:00:00", "08:40:00");

        await admin.PostJson($"/api/v1/routes/{first}/activate").ExpectAsync(HttpStatusCode.OK);
        await admin.PostJson($"/api/v1/routes/{second}/activate").ExpectAsync(HttpStatusCode.OK);
        var problem = await admin.PostJson($"/api/v1/routes/{third}/activate").ExpectAsync(HttpStatusCode.Conflict);

        problem.Code().ShouldBe("PlanRouteLimitReached");
        (await admin.Get($"/api/v1/routes/{third}").ExpectAsync(HttpStatusCode.OK))
            .GetProperty("status").GetString().ShouldBe("DRAFT");

        // Deactivating a route frees a slot.
        await admin.PostJson($"/api/v1/routes/{first}/deactivate").ExpectAsync(HttpStatusCode.OK);
        await admin.PostJson($"/api/v1/routes/{third}/activate").ExpectAsync(HttpStatusCode.OK);
        var types = await _app.WithDbAsync(db => db.Set<OutboxMessage>().Select(m => m.MessageType).ToListAsync());
        types.ShouldContain("administration.route-retired");
    }

    [Fact]
    public async Task AssignResources_DriverAlreadyOnAnOverlappingRoute_Returns409ScheduleConflict()
    {
        var admin = await _app.AdminAsync();
        var driverId = await admin.RegisterVerifiedDriverAsync(SeedAccounts.Driver);
        var vehicleId = await admin.RegisterVehicleAsync("ABC-123");
        await admin.ReadyRouteAsync(driverId, vehicleId, "Ruta 1", "06:45:00", "07:50:00");
        var other = await admin.CreateRouteAsync("Ruta 2", "07:30:00", "08:30:00");

        var problem = await admin.PutJson($"/api/v1/routes/{other}/assignment",
                new { driverId, vehicleId = await admin.RegisterVehicleAsync("XYZ-987") })
            .ExpectAsync(HttpStatusCode.Conflict);

        problem.Code().ShouldBe("ResourceScheduleConflict");
    }

    [Fact]
    public async Task AssignStudent_BeyondTheVehicleSeats_Returns409VehicleCapacityExceeded()
    {
        var admin = await _app.AdminAsync();
        var routeId = await admin.ReadyRouteAsync(await admin.RegisterVerifiedDriverAsync(SeedAccounts.Driver),
            await admin.RegisterVehicleAsync(capacity: 2), students: 2);
        var route = await admin.Get($"/api/v1/routes/{routeId}").ExpectAsync(HttpStatusCode.OK);

        var problem = await admin.PutJson($"/api/v1/routes/{routeId}/stops/{route.StopId(1)}/students/{Guid.NewGuid()}")
            .ExpectAsync(HttpStatusCode.Conflict);

        problem.Code().ShouldBe("VehicleCapacityExceeded");
    }

    [Fact]
    public async Task RegisterDriver_WithAParentAccount_Returns422DriverAccountNotValid()
    {
        var admin = await _app.AdminAsync();

        var problem = await admin.PostJson("/api/v1/roster/drivers", new
        {
            userId = SeedAccounts.Parent,
            firstName = "Rosita",
            lastName = "Nery",
            documentNumber = "41234567",
            licenseNumber = "Q41234567",
            licenseCategory = "A-IIb",
            licenseExpiresOn = "2030-01-01",
            phoneNumber = "987654321"
        }).ExpectAsync(HttpStatusCode.UnprocessableEntity);

        problem.Code().ShouldBe("DriverAccountNotValid");
    }

    [Fact]
    public async Task CreateRoute_InvalidData_Returns400()
    {
        var admin = await _app.AdminAsync();

        var badDirection = await admin.PostJson("/api/v1/routes", new
        {
            name = "Ruta X", direction = "CIRCULAR", departureTime = "06:45:00", estimatedArrivalTime = "07:00:00",
            operatingDays = new[] { "Monday" }
        }).ExpectAsync(HttpStatusCode.BadRequest);
        badDirection.Code().ShouldBe("InvalidAdministrationData");

        // Model binding: required name missing
        await admin.PostJson("/api/v1/routes", new { direction = "PICKUP" }).ExpectAsync(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task DriverWithActiveRoute_CannotBeSuspended_AndVehicleCannotBeDisabled()
    {
        var admin = await _app.AdminAsync();
        var driverId = await admin.RegisterVerifiedDriverAsync(SeedAccounts.Driver);
        var vehicleId = await admin.RegisterVehicleAsync();
        var routeId = await admin.ReadyRouteAsync(driverId, vehicleId);
        await admin.PostJson($"/api/v1/routes/{routeId}/activate").ExpectAsync(HttpStatusCode.OK);

        (await admin.PostJson($"/api/v1/roster/drivers/{driverId}/suspend").ExpectAsync(HttpStatusCode.Conflict))
            .Code().ShouldBe("DriverHasActiveRoutes");
        (await admin.PostJson($"/api/v1/roster/vehicles/{vehicleId}/disable").ExpectAsync(HttpStatusCode.Conflict))
            .Code().ShouldBe("VehicleHasActiveRoutes");
    }
}
