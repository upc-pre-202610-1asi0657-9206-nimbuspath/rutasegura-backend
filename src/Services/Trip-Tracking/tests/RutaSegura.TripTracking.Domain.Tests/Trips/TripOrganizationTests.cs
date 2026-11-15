using RutaSegura.TripTracking.Domain.Common;
using RutaSegura.TripTracking.Domain.Tests.Builders;
using RutaSegura.TripTracking.Domain.Trips;

namespace RutaSegura.TripTracking.Domain.Tests.Trips;

[Trait("Category", "Domain")]
public class TripOrganizationTests
{
    private static readonly OrganizationId SantaRosa = new(Guid.Parse("0a000000-0000-0000-0000-000000000001"));
    private static readonly OrganizationId Other = new(Guid.Parse("0b000000-0000-0000-0000-000000000002"));

    private static Trip ScheduleFor(OrganizationId organization) => Trip.Schedule(
        TripId.New(), TripBuilder.Route, "Ruta 3", TripDirection.Pickup, TripBuilder.Driver, TripBuilder.Vehicle,
        TripBuilder.ScheduledStart,
        [new TripStopDefinition(TripBuilder.Stop1, 1, "Av. Primavera 1240", TripBuilder.Stop1Position)],
        [new TripPassengerDefinition(TripBuilder.Valeria, TripBuilder.Stop1)],
        TripBuilder.ScheduledStart.AddHours(-12),
        organization);

    [Fact]
    public void Schedule_WithOrganization_BelongsOnlyToThatOrganization()
    {
        var trip = ScheduleFor(SantaRosa);

        trip.OrganizationId.ShouldBe(SantaRosa);
        trip.BelongsTo(SantaRosa).ShouldBeTrue();
        trip.BelongsTo(Other).ShouldBeFalse();
    }

    [Fact]
    public void Schedule_WithoutOrganization_IsNotRestricted()
    {
        var trip = new TripBuilder().Build();

        trip.OrganizationId.IsEmpty.ShouldBeTrue();
        trip.BelongsTo(Other).ShouldBeTrue();
    }
}
