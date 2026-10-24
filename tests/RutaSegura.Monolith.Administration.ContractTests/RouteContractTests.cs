using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Monolith.Administration.Domain.Model.Aggregates;
using Monolith.Administration.Domain.Model.ValueObjects;
using Monolith.Shared.Domain.Model.ValueObjects;
using RutaSegura.BuildingBlocks.Application.Validation;
using RutaSegura.TripTracking.Application.Abstractions;
using RutaSegura.TripTracking.Application.DependencyInjection;
using RutaSegura.TripTracking.Application.Replicas;
using AdminOutbound = Monolith.Administration.Application.IntegrationEvents.Outbound;
using TripDirection = RutaSegura.TripTracking.Domain.Trips.TripDirection;
using TripInbound = RutaSegura.TripTracking.Infrastructure.Messaging.Inbound;

namespace RutaSegura.Monolith.Administration.ContractTests;

/// <summary>
///     Administration → Trip &amp; Tracking: the route Administration publishes when it is activated or changed
///     must be readable by Trip &amp; Tracking and pass its own validation, so trips can be scheduled from it.
/// </summary>
public sealed class RouteContractTests
{
    // Same serializer settings both services use for RabbitMQ payloads (camelCase).
    private static readonly JsonSerializerOptions PublisherJson = new(JsonSerializerDefaults.Web);

    // Settings of Trip & Tracking's InboundMessageTranslator.
    private static readonly JsonSerializerOptions TripTrackingJson = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly OrganizationId Organization = new(Guid.Parse("11111111-1111-1111-1111-111111111111"));
    private static readonly DateTimeOffset Now = new(2026, 10, 23, 13, 0, 0, TimeSpan.Zero);
    private static readonly Guid DriverAccount = Guid.Parse("aa0e8400-e29b-41d4-a716-446655440000");

    [Fact]
    public void RoutePublished_IsReadByTripTrackingAndPassesItsValidation()
    {
        var route = ActiveRoute();
        var published = AdminOutbound.RouteIntegrationEventMapper.ToSnapshot(route, DriverAccount);

        var json = JsonSerializer.Serialize(published, PublisherJson);
        var received = JsonSerializer.Deserialize<TripInbound.RoutePublishedV1>(json, TripTrackingJson)!;
        var snapshot = ToTripTrackingSnapshot(received);

        Validate(new UpsertRouteSnapshotCommand(snapshot)).ShouldBeEmpty();
        snapshot.RouteId.ShouldBe(route.Id.Identifier);
        snapshot.OrganizationId.ShouldBe(Organization.Identifier);
        snapshot.DriverId.ShouldBe(DriverAccount);
        snapshot.VehicleId.ShouldBe(route.Assignment!.VehicleId.Identifier);
        snapshot.Direction.ShouldBe(TripDirection.Pickup);
        snapshot.Stops.Count.ShouldBe(2);
        snapshot.Students.Count.ShouldBe(3);
        snapshot.OperatesOn(new DateOnly(2026, 10, 26)).ShouldBeTrue(); // Monday
        snapshot.OperatesOn(new DateOnly(2026, 10, 25)).ShouldBeFalse(); // Sunday
        // 06:45 in Lima is 11:45 UTC: Trip & Tracking schedules the trip at the right instant.
        snapshot.ScheduledStartOn(new DateOnly(2026, 10, 26)).UtcDateTime
            .ShouldBe(new DateTime(2026, 10, 26, 11, 45, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void RoutePublished_ReturnRoute_UsesTripTrackingDirectionName()
    {
        var route = ActiveRoute(RouteDirection.CreateReturn());
        var json = JsonSerializer.Serialize(AdminOutbound.RouteIntegrationEventMapper.ToSnapshot(route, DriverAccount),
            PublisherJson);

        var received = JsonSerializer.Deserialize<TripInbound.RoutePublishedV1>(json, TripTrackingJson)!;

        Enum.Parse<TripDirection>(received.Direction, ignoreCase: true).ShouldBe(TripDirection.Return);
    }

    [Fact]
    public void RouteRetired_IsReadByTripTracking()
    {
        var routeId = Guid.NewGuid();
        var json = JsonSerializer.Serialize(new AdminOutbound.RouteRetiredV1(routeId), PublisherJson);

        JsonSerializer.Deserialize<TripInbound.RouteRetiredV1>(json, TripTrackingJson)!.RouteId.ShouldBe(routeId);
    }

    [Theory]
    [InlineData(AdminOutbound.AdministrationMessageTypes.RoutePublished, "route-published")]
    [InlineData(AdminOutbound.AdministrationMessageTypes.RouteUpdated, "route-updated")]
    [InlineData(AdminOutbound.AdministrationMessageTypes.RouteRetired, "route-retired")]
    public void MessageTypes_MatchTheSuffixesTripTrackingSubscribesTo(string messageType, string suffix)
    {
        // Trip & Tracking binds "administration.events" with "*.<suffix>" and translates by suffix.
        messageType.ShouldBe($"administration.{suffix}");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>Same translation as Trip &amp; Tracking's InboundMessageTranslator.ToSnapshot (internal there).</summary>
    private static RouteSnapshot ToTripTrackingSnapshot(TripInbound.RoutePublishedV1 m) => new(
        m.RouteId,
        m.Name,
        Enum.Parse<TripDirection>(m.Direction, ignoreCase: true),
        m.DriverId,
        m.VehicleId,
        TimeOnly.Parse(m.DepartureTime, CultureInfo.InvariantCulture),
        m.TimeZoneId,
        m.OperatingDays.Select(d => Enum.Parse<DayOfWeek>(d, ignoreCase: true)).Distinct().ToList(),
        m.Stops.Select(s => new RouteStopSnapshot(s.StopId, s.Sequence, s.Name, s.Latitude, s.Longitude)).ToList(),
        m.Students.Select(s => new RouteStudentSnapshot(s.StudentId, s.StopId)).ToList(),
        m.OrganizationId ?? Guid.Empty);

    /// <summary>Runs Trip &amp; Tracking's real validator, resolved from its own DI registration.</summary>
    private static List<string> Validate(UpsertRouteSnapshotCommand command)
    {
        using var provider = new ServiceCollection().AddTripTrackingApplication().BuildServiceProvider();
        var validator = provider.GetRequiredService<IValidator<UpsertRouteSnapshotCommand>>();
        return validator.Validate(command).Select(error => $"{error.Code}: {error.Message}").ToList();
    }

    private static Route ActiveRoute(RouteDirection? direction = null)
    {
        var today = DateOnly.FromDateTime(Now.UtcDateTime);
        var driver = new Driver(Organization, new UserId(DriverAccount), new FullName("Carlos", "Quispe"),
            new DocumentNumber("45678912"), new DriverLicense("Q45678912", "A-IIb", today.AddYears(2)),
            new PhoneNumber("987654321"));
        driver.Verify(today, Now);
        var vehicle = new Vehicle(Organization, new LicensePlate("ABC-123"), new SeatingCapacity(15),
            new VehicleModel("Toyota", "Hiace"));

        var route = new Route(Organization, new RouteName("Ruta 3 - Colegio San Agustin"),
            direction ?? RouteDirection.CreatePickup(),
            new RouteSchedule(new TimeOnly(6, 45), new TimeOnly(7, 50), "America/Lima",
                [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday]));
        var first = route.AddStop("Av. Primavera 456", "Av. Primavera 456, Surco", new GeoLocation(-12.11234, -76.98567));
        var second = route.AddStop("Calle Los Olivos 123", null, new GeoLocation(-12.10890, -76.99120));
        route.AssignStudentToStop(first.Id, new StudentId(Guid.NewGuid()), null);
        route.AssignStudentToStop(first.Id, new StudentId(Guid.NewGuid()), null);
        route.AssignStudentToStop(second.Id, new StudentId(Guid.NewGuid()), null);
        route.AssignResources(driver, vehicle, Now);
        route.Activate(driver, vehicle, 0, new PlanLimits("BASIC", 3), Now);
        return route;
    }
}
