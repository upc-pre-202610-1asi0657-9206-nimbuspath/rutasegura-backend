using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RutaSegura.TripTracking.Infrastructure.Persistence;
using RutaSegura.TripTracking.Integration.Tests.Infrastructure;

namespace RutaSegura.TripTracking.Integration.Tests.Api;

/// <summary>
/// Flujo completo por HTTP contra la API real + MySQL: US-10 a US-14, Outbox en la misma transacción,
/// idempotencia, privacidad del tutor (AC-01) y errores ProblemDetails con code estable.
/// </summary>
[Trait("Category", "Integration")]
public sealed class TripApiTests(TripTrackingApiFactory factory) : IClassFixture<TripTrackingApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static DateTimeOffset At(int hour, int minute, int second = 0) =>
        new(2026, 10, 5, hour, minute, second, TripTrackingApiFactory.Lima);

    private static object Action() => new { clientEventId = Guid.NewGuid() };

    [Fact]
    public async Task FullPickupTrip_IsPersistedWithTelemetryAndOutbox()
    {
        factory.RequireMySql();
        factory.Clock.SetUtcNow(At(6, 40));
        var seeded = await factory.SeedTripAsync();
        var driver = factory.CreateClientAs("DRIVER", seeded.DriverId);
        var trip = $"/api/v1/trips/{seeded.TripId}";

        (await driver.PostAsJsonAsync($"{trip}/start", Action(), Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var positions = await driver.PostAsJsonAsync("/api/v1/tracking/positions", new
        {
            tripId = seeded.TripId,
            readings = new[]
            {
                new { latitude = -12.1050, longitude = -77.0, recordedAt = At(6, 40, 5), accuracyMeters = 8.0, speedKmh = 20.0 },
                new { latitude = -12.1040, longitude = -77.0, recordedAt = At(6, 40, 10), accuracyMeters = 250.0, speedKmh = 20.0 } // baja precisión
            }
        }, Ct);
        var accepted = await positions.Content.ReadFromJsonAsync<JsonElement>(Ct);
        accepted.GetProperty("accepted").GetInt32().ShouldBe(1);
        accepted.GetProperty("ignored").GetInt32().ShouldBe(1);
        factory.Clock.SetUtcNow(At(6, 41));

        (await driver.PostAsJsonAsync($"{trip}/boardings", new { studentId = seeded.Student1, clientEventId = Guid.NewGuid() }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await driver.PostAsJsonAsync($"{trip}/absences", new { studentId = seeded.Student2, clientEventId = Guid.NewGuid() }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await driver.PostAsJsonAsync($"{trip}/incidents",
            new { clientEventId = Guid.NewGuid(), incidentType = "HEAVY_TRAFFIC", notifyGuardians = true }, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        factory.Clock.SetUtcNow(At(7, 30));
        var complete = await driver.PostAsJsonAsync($"{trip}/complete", Action(), Ct);
        (await complete.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("tripStatus").GetString().ShouldBe("Completed");

        var summary = await driver.GetFromJsonAsync<JsonElement>($"{trip}/summary", Ct);
        summary.GetProperty("delivered").GetInt32().ShouldBe(1);
        summary.GetProperty("incidents").GetArrayLength().ShouldBe(1);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TripTrackingDbContext>();
        (await db.TrackPoints.CountAsync(p => p.TripId == seeded.TripId, Ct)).ShouldBe(1);
        var types = await db.OutboxMessages.Where(m => m.AggregateId == seeded.TripId)
            .OrderBy(m => m.OccurredAt).ThenBy(m => m.CreatedAt).Select(m => m.MessageType).ToListAsync(Ct);
        types.ShouldBe(
        [
            "trip-tracking.trip-scheduled", "trip-tracking.trip-started", "trip-tracking.vehicle-approaching-stop",
            "trip-tracking.student-boarded", "trip-tracking.student-marked-absent", "trip-tracking.incident-reported",
            "trip-tracking.trip-delayed", "trip-tracking.trip-completed"
        ]);
    }

    [Fact]
    public async Task RepeatedClientEventId_IsAppliedOnlyOnce()
    {
        factory.RequireMySql();
        factory.Clock.SetUtcNow(At(6, 40));
        var seeded = await factory.SeedTripAsync();
        var driver = factory.CreateClientAs("DRIVER", seeded.DriverId);
        var trip = $"/api/v1/trips/{seeded.TripId}";
        await driver.PostAsJsonAsync($"{trip}/start", Action(), Ct);

        var boarding = new { studentId = seeded.Student1, clientEventId = Guid.NewGuid() };
        var first = await (await driver.PostAsJsonAsync($"{trip}/boardings", boarding, Ct)).Content.ReadFromJsonAsync<JsonElement>(Ct);
        var retry = await (await driver.PostAsJsonAsync($"{trip}/boardings", boarding, Ct)).Content.ReadFromJsonAsync<JsonElement>(Ct);

        first.GetProperty("applied").GetBoolean().ShouldBeTrue();
        retry.GetProperty("applied").GetBoolean().ShouldBeFalse();

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TripTrackingDbContext>();
        (await db.OutboxMessages.CountAsync(m => m.AggregateId == seeded.TripId && m.MessageType == "trip-tracking.student-boarded", Ct))
            .ShouldBe(1);
    }

    [Fact]
    public async Task BusinessRuleViolation_ReturnsProblemDetailsWithStableCode()
    {
        factory.RequireMySql();
        factory.Clock.SetUtcNow(At(6, 40));
        var seeded = await factory.SeedTripAsync();
        var driver = factory.CreateClientAs("DRIVER", seeded.DriverId);
        await driver.PostAsJsonAsync($"/api/v1/trips/{seeded.TripId}/start", Action(), Ct);

        var response = await driver.PostAsJsonAsync($"/api/v1/trips/{seeded.TripId}/complete", Action(), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        problem.GetProperty("code").GetString().ShouldBe("TRIP_HAS_PENDING_STUDENTS");
    }

    [Fact]
    public async Task Security_RolesAndOwnershipAreEnforced()
    {
        factory.RequireMySql();
        factory.Clock.SetUtcNow(At(6, 40));
        var seeded = await factory.SeedTripAsync();
        var live = $"/api/v1/trips/{seeded.TripId}/live";

        (await factory.CreateClient().GetAsync(live, Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var guardian = factory.CreateClientAs("PARENT", seeded.GuardianId);
        (await guardian.PostAsJsonAsync($"/api/v1/trips/{seeded.TripId}/start", Action(), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var otherDriver = factory.CreateClientAs("DRIVER", Guid.NewGuid());
        var mismatch = await otherDriver.PostAsJsonAsync($"/api/v1/trips/{seeded.TripId}/start", Action(), Ct);
        mismatch.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await mismatch.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString().ShouldBe("TRIP_DRIVER_MISMATCH");

        var strangerGuardian = factory.CreateClientAs("PARENT", Guid.NewGuid());
        var denied = await strangerGuardian.GetAsync(live, Ct);
        denied.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await denied.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString().ShouldBe("TRIP_ACCESS_DENIED");

        var otherCompany = factory.CreateClientAs("ADMIN", Guid.NewGuid(), Guid.NewGuid());
        (await otherCompany.GetAsync(live, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var ownCompany = factory.CreateClientAs("ADMIN", Guid.NewGuid(), seeded.OrganizationId);
        (await ownCompany.GetAsync(live, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Guardian_SeesOnlyOwnChildAndNoPositionUntilTheTripStarts()
    {
        factory.RequireMySql();
        factory.Clock.SetUtcNow(At(6, 40));
        var seeded = await factory.SeedTripAsync();
        var guardian = factory.CreateClientAs("PARENT", seeded.GuardianId);

        var before = await guardian.GetFromJsonAsync<JsonElement>($"/api/v1/trips/{seeded.TripId}/live", Ct);
        before.GetProperty("position").ValueKind.ShouldBe(JsonValueKind.Null);
        before.GetProperty("passengers").GetArrayLength().ShouldBe(1);

        var driver = factory.CreateClientAs("DRIVER", seeded.DriverId);
        await driver.PostAsJsonAsync($"/api/v1/trips/{seeded.TripId}/start", Action(), Ct);
        await driver.PostAsJsonAsync("/api/v1/tracking/positions", new
        {
            tripId = seeded.TripId,
            readings = new[] { new { latitude = -12.1050, longitude = -77.0, recordedAt = At(6, 40, 5), accuracyMeters = 8.0 } }
        }, Ct);

        var during = await guardian.GetFromJsonAsync<JsonElement>($"/api/v1/trips/{seeded.TripId}/position", Ct);
        during.GetProperty("position").GetProperty("latitude").GetDouble().ShouldBe(-12.1050);

        var myTrips = await guardian.GetFromJsonAsync<JsonElement>(
            $"/api/v1/guardians/me/trips?from={Uri.EscapeDataString(At(0, 0).ToString("O"))}&to={Uri.EscapeDataString(At(23, 59).ToString("O"))}", Ct);
        myTrips.GetArrayLength().ShouldBe(1);
        myTrips[0].GetProperty("myStudents")[0].GetProperty("studentId").GetGuid().ShouldBe(seeded.Student1);
    }

    [Fact]
    public async Task OfflineSync_AppliesInRealOrderAndReportsRejected()
    {
        factory.RequireMySql();
        factory.Clock.SetUtcNow(At(6, 40));
        var seeded = await factory.SeedTripAsync();
        var driver = factory.CreateClientAs("DRIVER", seeded.DriverId);
        await driver.PostAsJsonAsync($"/api/v1/trips/{seeded.TripId}/start", Action(), Ct);
        factory.Clock.SetUtcNow(At(7, 0));

        var response = await driver.PostAsJsonAsync($"/api/v1/trips/{seeded.TripId}/sync", new
        {
            events = new object[]
            {
                new { clientEventId = Guid.NewGuid(), type = "Alighting", occurredAt = At(6, 55), studentId = seeded.Student1 },
                new { clientEventId = Guid.NewGuid(), type = "Boarding", occurredAt = At(6, 50), studentId = seeded.Student1 },
                new { clientEventId = Guid.NewGuid(), type = "Boarding", occurredAt = At(6, 51), studentId = Guid.NewGuid() }
            }
        }, Ct);

        var outcome = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        outcome.GetProperty("applied").GetInt32().ShouldBe(2);
        outcome.GetProperty("outcomes")[2].GetProperty("errorCode").GetString().ShouldBe("STUDENT_NOT_IN_ROSTER");
    }

    [Fact]
    public async Task Admin_SchedulesListsAndCancelsTripsOfTheCompany()
    {
        factory.RequireMySql();
        factory.Clock.SetUtcNow(At(6, 0));
        var seeded = await factory.SeedTripAsync();
        var admin = factory.CreateClientAs("ADMIN", Guid.NewGuid(), seeded.OrganizationId);

        var scheduled = await admin.PostAsJsonAsync("/api/v1/trips", new { routeId = seeded.RouteId, serviceDate = "2026-10-06" }, Ct);
        scheduled.StatusCode.ShouldBe(HttpStatusCode.Created);
        var tomorrowTrip = (await scheduled.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("tripId").GetGuid();

        var list = await admin.GetFromJsonAsync<JsonElement>(
            $"/api/v1/trips?from={Uri.EscapeDataString(At(0, 0).ToString("O"))}&to={Uri.EscapeDataString(At(0, 0).AddDays(2).ToString("O"))}", Ct);
        list.GetArrayLength().ShouldBe(2);

        (await admin.PostAsJsonAsync($"/api/v1/trips/{tomorrowTrip}/cancel", new { reason = "Feriado escolar" }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var cancelled = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/trips?status=Cancelled&from={Uri.EscapeDataString(At(0, 0).ToString("O"))}&to={Uri.EscapeDataString(At(0, 0).AddDays(2).ToString("O"))}", Ct);
        cancelled.GetArrayLength().ShouldBe(1);
    }

    [Fact]
    public async Task Readiness_ReportsHealthyWhenMySqlResponds()
    {
        factory.RequireMySql();

        var response = await factory.CreateClient().GetAsync("/health/ready", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(Ct)).ShouldBe("Healthy");
    }
}
