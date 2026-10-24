using System.Net;
using RutaSegura.Monolith.Administration.IntegrationTests.Support;

namespace RutaSegura.Monolith.Administration.IntegrationTests;

/// <summary>
///     The Route aggregate is saved and reloaded as a whole: new, edited, reordered and removed stops, students per
///     stop (JSON column) and the optional assignment (complex type) survive a round trip to the database.
/// </summary>
public sealed class RouteAggregatePersistenceTests : IDisposable
{
    private readonly MonolithApp _app = new();

    public void Dispose() => _app.Dispose();

    [Fact]
    public async Task EditStopsAndStudents_EachChangeIsPersisted()
    {
        var admin = await _app.AdminAsync();
        var routeId = await admin.CreateRouteAsync();
        var route = await admin.AddTwoStopsAsync(routeId);
        var first = route.StopId(0);
        var second = route.StopId(1);
        var student = Guid.NewGuid();

        // Insert a stop at position 1 in an already persisted route (new child after load).
        var withThird = await admin.PostJson($"/api/v1/routes/{routeId}/stops",
                new { name = "Parque Kennedy", latitude = -12.1211, longitude = -77.0297, position = 1 })
            .ExpectAsync(HttpStatusCode.OK);
        var third = withThird.StopId(0);

        await admin.PutJson($"/api/v1/routes/{routeId}/stops/{first}/students/{student}").ExpectAsync(HttpStatusCode.OK);
        await admin.PutJson($"/api/v1/routes/{routeId}/stops/{second}/students/{student}").ExpectAsync(HttpStatusCode.OK);
        await admin.PutJson($"/api/v1/routes/{routeId}/stops/order", new { stopIds = new[] { second, first, third } })
            .ExpectAsync(HttpStatusCode.OK);
        await admin.PutJson($"/api/v1/routes/{routeId}/stops/{first}",
                new { name = "Av. Primavera 500", address = "Surco", latitude = -12.11300, longitude = -76.98600 })
            .ExpectAsync(HttpStatusCode.OK);
        await admin.Delete($"/api/v1/routes/{routeId}/stops/{third}").ExpectAsync(HttpStatusCode.OK);

        var reloaded = await admin.Get($"/api/v1/routes/{routeId}").ExpectAsync(HttpStatusCode.OK);
        var stops = reloaded.GetProperty("stops");
        stops.GetArrayLength().ShouldBe(2);
        stops[0].GetProperty("id").GetGuid().ShouldBe(second);
        stops[0].GetProperty("sequence").GetInt32().ShouldBe(1);
        stops[0].GetProperty("studentIds")[0].GetGuid().ShouldBe(student); // moved from the first stop
        stops[1].GetProperty("id").GetGuid().ShouldBe(first);
        stops[1].GetProperty("name").GetString().ShouldBe("Av. Primavera 500");
        stops[1].GetProperty("studentIds").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task AssignAndReleaseResources_OptionalAssignmentIsPersisted()
    {
        var admin = await _app.AdminAsync();
        var driverId = await admin.RegisterVerifiedDriverAsync(SeedAccounts.Driver);
        var vehicleId = await admin.RegisterVehicleAsync();
        var routeId = await admin.ReadyRouteAsync(driverId, vehicleId);

        (await admin.Get($"/api/v1/routes/{routeId}").ExpectAsync(HttpStatusCode.OK))
            .GetProperty("assignment").GetProperty("vehicleId").GetGuid().ShouldBe(vehicleId);
        (await admin.Get($"/api/v1/roster/drivers/{driverId}/routes").ExpectAsync(HttpStatusCode.OK))
            .GetArrayLength().ShouldBe(1);

        await admin.Delete($"/api/v1/routes/{routeId}/assignment").ExpectAsync(HttpStatusCode.OK);

        (await admin.Get($"/api/v1/routes/{routeId}").ExpectAsync(HttpStatusCode.OK))
            .GetProperty("assignment").ValueKind.ShouldBe(System.Text.Json.JsonValueKind.Null);
        (await admin.Get($"/api/v1/roster/drivers/{driverId}/routes").ExpectAsync(HttpStatusCode.OK))
            .GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task DeleteRoute_RemovesItWithItsStops()
    {
        var admin = await _app.AdminAsync();
        var routeId = await admin.CreateRouteAsync();
        await admin.AddTwoStopsAsync(routeId);

        await admin.Delete($"/api/v1/routes/{routeId}").ExpectAsync(HttpStatusCode.NoContent);

        (await admin.Get($"/api/v1/routes/{routeId}").ExpectAsync(HttpStatusCode.NotFound))
            .Code().ShouldBe("RouteNotFound");
    }

    [Fact]
    public async Task RosterChanges_ArePersisted()
    {
        var admin = await _app.AdminAsync();
        var driverId = await admin.RegisterVerifiedDriverAsync(SeedAccounts.Driver);
        var vehicleId = await admin.RegisterVehicleAsync(capacity: 15);

        await admin.PutJson($"/api/v1/roster/drivers/{driverId}/contact", new { phoneNumber = "+51 912 345 678" })
            .ExpectAsync(HttpStatusCode.OK);
        await admin.PutJson($"/api/v1/roster/drivers/{driverId}/license",
            new { licenseNumber = "Q99887766", licenseCategory = "A-IIIa", licenseExpiresOn = "2031-06-30" })
            .ExpectAsync(HttpStatusCode.OK);
        await admin.PutJson($"/api/v1/roster/vehicles/{vehicleId}/capacity", new { seatingCapacity = 20 })
            .ExpectAsync(HttpStatusCode.OK);
        await admin.PostJson($"/api/v1/roster/vehicles/{vehicleId}/disable").ExpectAsync(HttpStatusCode.OK);

        var driver = await admin.Get($"/api/v1/roster/drivers/{driverId}").ExpectAsync(HttpStatusCode.OK);
        driver.GetProperty("phoneNumber").GetString().ShouldBe("+51912345678");
        driver.GetProperty("licenseCategory").GetString().ShouldBe("A-IIIA");
        driver.GetProperty("licenseExpiresOn").GetString().ShouldBe("2031-06-30");
        var vehicle = await admin.Get($"/api/v1/roster/vehicles/{vehicleId}").ExpectAsync(HttpStatusCode.OK);
        vehicle.GetProperty("seatingCapacity").GetInt32().ShouldBe(20);
        vehicle.GetProperty("status").GetString().ShouldBe("DISABLED");
        vehicle.GetProperty("licensePlate").GetString().ShouldBe("ABC-123");
    }
}
