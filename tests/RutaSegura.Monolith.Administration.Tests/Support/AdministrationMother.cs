using Monolith.Administration.Domain.Model.Aggregates;
using Monolith.Administration.Domain.Model.ValueObjects;
using Monolith.Shared.Domain.Model.ValueObjects;

namespace RutaSegura.Monolith.Administration.Tests.Support;

/// <summary>Object Mother: a school transport operation in Lima ("Ruta 3" of the prototype).</summary>
internal static class AdministrationMother
{
    public static readonly OrganizationId Organization = new(Guid.Parse("11111111-1111-1111-1111-111111111111"));
    public static readonly OrganizationId OtherOrganization = new(Guid.Parse("22222222-2222-2222-2222-222222222222"));

    /// <summary>Friday 23 Oct 2026, 08:00 in Lima (UTC-5).</summary>
    public static readonly DateTimeOffset Now = new(2026, 10, 23, 13, 0, 0, TimeSpan.Zero);

    public static readonly DateOnly Today = new(2026, 10, 23);

    public static readonly DayOfWeek[] Weekdays =
        [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday];

    private static int _sequence;

    public static Driver NewDriver(OrganizationId? organization = null, bool verified = true,
        DateOnly? licenseExpiresOn = null, Guid? userId = null)
    {
        var number = Interlocked.Increment(ref _sequence);
        var driver = new Driver(
            organization ?? Organization,
            new UserId(userId ?? Guid.NewGuid()),
            new FullName("Carlos", "Quispe"),
            new DocumentNumber($"{40000000 + number}"),
            new DriverLicense($"Q{10000000 + number}", "A-IIb", licenseExpiresOn ?? Today.AddYears(2)),
            new PhoneNumber("987 654 321"));
        if (verified) driver.Verify(Today, Now);
        return driver;
    }

    public static Vehicle NewVehicle(OrganizationId? organization = null, int capacity = 15)
    {
        var number = Interlocked.Increment(ref _sequence) % 1000;
        return new Vehicle(organization ?? Organization, new LicensePlate($"ABC{number:000}"),
            new SeatingCapacity(capacity), new VehicleModel("Toyota", "Hiace"));
    }

    /// <summary>06:45 - 07:50 on weekdays unless other days are given.</summary>
    public static RouteSchedule Morning(params DayOfWeek[] days) =>
        Schedule(new TimeOnly(6, 45), new TimeOnly(7, 50), days);

    public static RouteSchedule Schedule(TimeOnly departure, TimeOnly arrival, params DayOfWeek[] days) =>
        new(departure, arrival, RouteSchedule.DefaultTimeZoneId, days.Length == 0 ? Weekdays : days);

    public static Route NewRoute(string name = "Ruta 3 - Colegio San Agustin", RouteSchedule? schedule = null,
        OrganizationId? organization = null) =>
        new(organization ?? Organization, new RouteName(name), RouteDirection.CreatePickup(), schedule ?? Morning());

    /// <summary>Stops about 550 m apart along Av. Primavera (Surco).</summary>
    public static GeoLocation StopLocation(int index) => new(-12.1100 - index * 0.005, -76.9850);

    /// <summary>A route with two stops, <paramref name="students" /> students and the given resources assigned.</summary>
    public static Route ReadyRoute(Driver driver, Vehicle vehicle, int students = 2, RouteSchedule? schedule = null,
        string name = "Ruta 3 - Colegio San Agustin")
    {
        var route = NewRoute(name, schedule, driver.OrganizationId);
        var first = route.AddStop("Av. Primavera 456", "Av. Primavera 456, Surco", StopLocation(0));
        var second = route.AddStop("Calle Los Olivos 123", null, StopLocation(1));
        for (var i = 0; i < students; i++)
            route.AssignStudentToStop(i % 2 == 0 ? first.Id : second.Id, new StudentId(Guid.NewGuid()), null);
        route.AssignResources(driver, vehicle, Now);
        route.ClearDomainEvents();
        return route;
    }

    public static PlanLimits Plan(int maxActiveRoutes = 3) => new("BASIC", maxActiveRoutes);
}
