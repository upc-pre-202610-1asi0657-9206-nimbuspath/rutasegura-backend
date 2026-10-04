using RutaSegura.TripTracking.Domain.Access;
using RutaSegura.TripTracking.Domain.Common;
using RutaSegura.TripTracking.Domain.Tests.Builders;
using static RutaSegura.TripTracking.Domain.Tests.Builders.TripBuilder;

namespace RutaSegura.TripTracking.Domain.Tests.Access;

/// <summary>AC-01 Privacidad de menores.</summary>
[Trait("Category", "Unit")]
public class GuardianAccessPolicyTests
{
    [Fact]
    public void CanViewTrip_GuardianWithChildInTrip_ReturnsTrue()
    {
        var trip = new TripBuilder().BuildStarted();

        GuardianAccessPolicy.CanViewTrip(trip, [Valeria]).ShouldBeTrue();
    }

    [Fact]
    public void CanViewTrip_GuardianWithoutChildInTrip_ReturnsFalse()
    {
        var trip = new TripBuilder().BuildStarted();

        GuardianAccessPolicy.CanViewTrip(trip, [Stranger]).ShouldBeFalse();
        GuardianAccessPolicy.CanViewTrip(trip, []).ShouldBeFalse();
    }

    [Fact]
    public void VisiblePassengers_OnlyReturnsGuardiansOwnChildren()
    {
        var trip = new TripBuilder().BuildStarted();

        var visible = GuardianAccessPolicy.VisiblePassengers(trip, [Camila, Stranger]);

        visible.ShouldHaveSingleItem().StudentId.ShouldBe(Camila);
    }

    [Fact]
    public void CanSeeLivePosition_OnlyWhileTripIsActive()
    {
        var scheduled = new TripBuilder().Build();
        var started = new TripBuilder().BuildStarted();
        var completed = new TripBuilder().BuildStarted();
        foreach (var student in new[] { Valeria, Mateo, Camila })
            completed.MarkStudentAbsent(Driver, student, ClientEventId.New(), null, At(6, 45));
        completed.Complete(Driver, ClientEventId.New(), At(7, 0));

        GuardianAccessPolicy.CanSeeLivePosition(scheduled).ShouldBeFalse();
        GuardianAccessPolicy.CanSeeLivePosition(started).ShouldBeTrue();
        GuardianAccessPolicy.CanSeeLivePosition(completed).ShouldBeFalse();
    }
}
