using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using RutaSegura.TripTracking.Api.Controllers;
using RutaSegura.TripTracking.Application.Abstractions;
using RutaSegura.TripTracking.Domain.Common;
using RutaSegura.TripTracking.Domain.Tracking;
using RutaSegura.TripTracking.Domain.Trips;

namespace RutaSegura.TripTracking.Integration.Tests.Api;

public sealed class NotificationContextTests
{
    private const string Token = "notification-internal-test-token-0123456789";

    [Theory]
    [InlineData(null, null)]
    [InlineData(null, Token)]
    [InlineData(Token, null)]
    [InlineData(Token, "incorrect")]
    public async Task DisabledOrUnauthorizedServiceNeverReadsTheDatabase(string? configured, string? supplied)
    {
        var repository = new Repository();
        var controller = Controller(repository, configured, supplied);
        (await controller.Get(Guid.NewGuid(), Guid.NewGuid(), TestContext.Current.CancellationToken)).ShouldBeOfType<UnauthorizedResult>();
        repository.Reads.ShouldBe(0);
    }

    [Fact]
    public async Task AuthenticatedServiceCannotQueryAnotherOrganizationsTrip()
    {
        var repository = new Repository { Trip = TripFor(Guid.NewGuid(), active: true) };
        (await Controller(repository, Token, Token).Get(repository.Trip.Id.Value, Guid.NewGuid(), TestContext.Current.CancellationToken))
            .ShouldBeOfType<NotFoundResult>();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ContextOnlyExposesTripTenantAndActiveState(bool active)
    {
        var org = Guid.NewGuid();
        var repository = new Repository { Trip = TripFor(org, active) };
        var result = (await Controller(repository, Token, Token).Get(repository.Trip.Id.Value, org, TestContext.Current.CancellationToken)).ShouldBeOfType<OkObjectResult>();
        var json = System.Text.Json.JsonSerializer.SerializeToElement(result.Value);
        json.GetProperty("isActive").GetBoolean().ShouldBe(active);
        json.GetProperty("organizationId").GetGuid().ShouldBe(org);
        json.EnumerateObject().Count().ShouldBe(3); // No GPS, nombres ni identificadores de alumnos.
    }

    private static NotificationContextController Controller(Repository repository, string? expected, string? supplied)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["InternalServices:NotificationToken"] = expected }).Build();
        var controller = new NotificationContextController(configuration, repository) { ControllerContext = new() { HttpContext = new DefaultHttpContext() } };
        if (supplied is not null) controller.Request.Headers["X-Service-Token"] = supplied;
        return controller;
    }

    private static Trip TripFor(Guid organization, bool active)
    {
        var driver = new DriverId(Guid.NewGuid());
        var now = DateTimeOffset.UtcNow;
        var trip = Trip.Schedule(TripId.New(), new RouteId(Guid.NewGuid()), "Ruta de prueba", TripDirection.Pickup, driver,
            new VehicleId(Guid.NewGuid()), now, [new(new StopId(Guid.NewGuid()), 1, "Parada", GeoPosition.Create(-12.1, -77))], [], now, new OrganizationId(organization));
        if (active) trip.Start(driver, ClientEventId.New(), now);
        return trip;
    }

    private sealed class Repository : ITripRepository
    {
        public Trip? Trip { get; init; }
        public int Reads { get; private set; }
        public Task<Trip?> GetByIdAsync(TripId id, CancellationToken cancellationToken) { Reads++; return Task.FromResult(Trip?.Id == id ? Trip : null); }
        public Task<Trip?> FindByRouteAndScheduledStartAsync(RouteId routeId, DateTimeOffset scheduledStart, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<Trip>> ListByDriverAsync(DriverId driverId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<Trip>> ListByStudentsAsync(IReadOnlyCollection<StudentId> students, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<Trip>> ListByOrganizationAsync(OrganizationId organizationId, DateTimeOffset from, DateTimeOffset to, TripStatus? status, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<Trip>> ListScheduledByRouteAsync(RouteId routeId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public void Add(Trip trip) => throw new NotSupportedException();
    }
}
