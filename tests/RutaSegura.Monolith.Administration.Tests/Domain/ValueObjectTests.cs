using Monolith.Administration.Domain.Model.ValueObjects;

namespace RutaSegura.Monolith.Administration.Tests.Domain;

public sealed class ValueObjectTests
{
    [Theory]
    [InlineData("1234567")]
    [InlineData("1234567890123")]
    [InlineData("12-345678")]
    public void DocumentNumber_InvalidFormat_Throws(string value) =>
        Should.Throw<ArgumentException>(() => new DocumentNumber(value));

    [Fact]
    public void LicensePlate_DashesSpacesAndCase_AreTheSamePlate()
    {
        var withDash = new LicensePlate("abc-123");
        var compact = new LicensePlate(" ABC123 ");

        withDash.ShouldBe(compact);
        withDash.Value.ShouldBe("ABC123");
        withDash.Display.ShouldBe("ABC-123");
    }

    [Fact]
    public void PhoneNumber_WithSpaces_IsNormalized() =>
        new PhoneNumber("+51 987 654 321").Value.ShouldBe("+51987654321");

    [Theory]
    [InlineData("12345")]
    [InlineData("phone")]
    public void PhoneNumber_Invalid_Throws(string value) =>
        Should.Throw<ArgumentException>(() => new PhoneNumber(value));

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-91, -77)]
    [InlineData(-12, 181)]
    public void GeoLocation_InvalidCoordinates_Throw(double latitude, double longitude) =>
        Should.Throw<ArgumentException>(() => new GeoLocation(latitude, longitude));

    [Fact]
    public void GeoLocation_DistanceInMetersTo_UsesHaversine()
    {
        // 0.005° of latitude ≈ 556 m
        var distance = new GeoLocation(-12.1100, -76.9850).DistanceInMetersTo(new GeoLocation(-12.1150, -76.9850));

        distance.ShouldBeInRange(550, 560);
    }

    [Fact]
    public void SeatingCapacity_OutOfRange_Throws()
    {
        Should.Throw<ArgumentException>(() => new SeatingCapacity(0));
        Should.Throw<ArgumentException>(() => new SeatingCapacity(61));
    }

    [Fact]
    public void DriverLicense_IsValidUntilAndIncludingItsExpirationDate()
    {
        var license = new DriverLicense("Q12345678", "a-iib", new DateOnly(2026, 10, 23));

        license.Category.ShouldBe("A-IIB");
        license.IsValidOn(new DateOnly(2026, 10, 23)).ShouldBeTrue();
        license.IsValidOn(new DateOnly(2026, 10, 24)).ShouldBeFalse();
    }

    [Fact]
    public void DriverLicense_UnknownCategory_Throws() =>
        Should.Throw<ArgumentException>(() => new DriverLicense("Q12345678", "B-I", new DateOnly(2030, 1, 1)));

    [Fact]
    public void RouteDirection_DefinesWhatHappensAtTheStops()
    {
        new RouteDirection("pickup").StopPurpose.ShouldBe("PICK_UP");
        RouteDirection.CreateReturn().StopPurpose.ShouldBe("DROP_OFF");
        Should.Throw<ArgumentException>(() => new RouteDirection("ROUND_TRIP"));
    }
}
