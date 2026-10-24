using Monolith.Administration.Domain.Model.Aggregates;
using Monolith.Administration.Domain.Model.ValueObjects;
using static RutaSegura.Monolith.Administration.Tests.Support.AdministrationMother;

namespace RutaSegura.Monolith.Administration.Tests.Domain;

public sealed class TripRecordTests
{
    private static TripRecord Record(int delivered, int absent, DateTimeOffset startedAt, DateTimeOffset completedAt) =>
        new(Organization, new TripId(Guid.NewGuid()), RouteId.New(), "Ruta 3", delivered, absent, 1, startedAt,
            completedAt);

    [Fact]
    public void New_CompletedTrip_ExposesDurationAndAttendance()
    {
        var tripRecord = Record(delivered: 9, absent: 1, Now, Now.AddMinutes(62));

        tripRecord.Duration.ShouldBe(TimeSpan.FromMinutes(62));
        tripRecord.AttendanceRate.ShouldBe(0.9, tolerance: 0.0001);
    }

    [Fact]
    public void New_CompletedBeforeStarted_Throws() =>
        Should.Throw<ArgumentException>(() => Record(1, 0, Now, Now.AddMinutes(-1)));

    [Fact]
    public void New_NegativeCounters_Throw() =>
        Should.Throw<ArgumentException>(() => Record(-1, 0, Now, Now));

    [Fact]
    public void New_EmptyTripId_Throws() =>
        Should.Throw<ArgumentException>(() => new TripId(Guid.Empty));
}
