using Monolith.Administration.Domain.Model.ValueObjects;
using RutaSegura.Monolith.Administration.Tests.Support;

namespace RutaSegura.Monolith.Administration.Tests.Domain;

public sealed class RouteScheduleTests
{
    private static RouteSchedule At(int fromHour, int fromMinute, int toHour, int toMinute, params DayOfWeek[] days) =>
        AdministrationMother.Schedule(new TimeOnly(fromHour, fromMinute), new TimeOnly(toHour, toMinute), days);

    [Fact]
    public void Constructor_ArrivalNotAfterDeparture_Throws() =>
        Should.Throw<ArgumentException>(() => At(7, 50, 6, 45));

    [Fact]
    public void Constructor_WithoutOperatingDays_Throws() =>
        Should.Throw<ArgumentException>(() =>
            new RouteSchedule(new TimeOnly(6, 45), new TimeOnly(7, 50), "America/Lima", []));

    [Fact]
    public void Constructor_UnknownTimeZone_Throws() =>
        Should.Throw<ArgumentException>(() =>
            new RouteSchedule(new TimeOnly(6, 45), new TimeOnly(7, 50), "Mars/Olympus", [DayOfWeek.Monday]));

    [Fact]
    public void OperatingDays_RoundTripThroughTheMask()
    {
        var schedule = At(6, 45, 7, 50, DayOfWeek.Friday, DayOfWeek.Monday, DayOfWeek.Monday);

        schedule.OperatingDays.ShouldBe([DayOfWeek.Monday, DayOfWeek.Friday], ignoreOrder: false);
        schedule.OperatesOn(DayOfWeek.Monday).ShouldBeTrue();
        schedule.OperatesOn(DayOfWeek.Sunday).ShouldBeFalse();
        schedule.Duration.ShouldBe(TimeSpan.FromMinutes(65));
    }

    [Fact]
    public void OverlapsWith_SameDayAndIntersectingWindow_IsTrue() =>
        At(6, 45, 7, 50).OverlapsWith(At(7, 30, 8, 30)).ShouldBeTrue();

    [Fact]
    public void OverlapsWith_BackToBackRoutes_IsFalse() =>
        At(6, 45, 7, 50).OverlapsWith(At(7, 50, 8, 30)).ShouldBeFalse();

    [Fact]
    public void OverlapsWith_SameHoursOnDifferentDays_IsFalse() =>
        At(6, 45, 7, 50, DayOfWeek.Monday).OverlapsWith(At(6, 45, 7, 50, DayOfWeek.Tuesday)).ShouldBeFalse();

    [Fact]
    public void LocalDateOf_UsesTheTimeZoneOfTheRoute() =>
        // 02:00 UTC on Saturday is still Friday 21:00 in Lima
        At(6, 45, 7, 50).LocalDateOf(new DateTimeOffset(2026, 10, 24, 2, 0, 0, TimeSpan.Zero))
            .ShouldBe(new DateOnly(2026, 10, 23));
}
