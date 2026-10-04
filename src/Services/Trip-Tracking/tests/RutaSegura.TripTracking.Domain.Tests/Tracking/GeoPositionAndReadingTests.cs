using RutaSegura.BuildingBlocks.Domain;
using RutaSegura.TripTracking.Domain.Common;
using RutaSegura.TripTracking.Domain.Tracking;

namespace RutaSegura.TripTracking.Domain.Tests.Tracking;

[Trait("Category", "Unit")]
public class GeoPositionTests
{
    [Fact]
    public void Create_ValidLimaCoordinates_KeepsValues()
    {
        var position = GeoPosition.Create(-12.0464, -77.0428);

        position.Latitude.ShouldBe(-12.0464);
        position.Longitude.ShouldBe(-77.0428);
    }

    [Theory]
    [InlineData(-90.1, -77.0)]
    [InlineData(90.1, -77.0)]
    [InlineData(-12.0, -180.1)]
    [InlineData(-12.0, 180.1)]
    [InlineData(double.NaN, -77.0)]
    [InlineData(0, 0)] // Null Island: GPS sin señal
    public void Create_InvalidCoordinates_ThrowsInvalidCoordinates(double latitude, double longitude)
    {
        var ex = Should.Throw<DomainException>(() => GeoPosition.Create(latitude, longitude));

        ex.Code.ShouldBe(TrackingErrors.InvalidCoordinates);
    }

    [Fact]
    public void DistanceMetersTo_OneHundredthOfDegreeOfLatitude_IsAbout1112Meters()
    {
        var a = GeoPosition.Create(-12.10, -77.0);
        var b = GeoPosition.Create(-12.09, -77.0);

        a.DistanceMetersTo(b).ShouldBe(1111.95, tolerance: 1);
    }

    [Fact]
    public void DistanceMetersTo_SamePoint_IsZero()
    {
        var a = GeoPosition.Create(-12.10, -77.0);

        a.DistanceMetersTo(GeoPosition.Create(-12.10, -77.0)).ShouldBe(0, tolerance: 0.001);
    }

    [Fact]
    public void Equality_SameCoordinates_AreEqualValueObjects()
    {
        GeoPosition.Create(-12.1, -77.0).ShouldBe(GeoPosition.Create(-12.1, -77.0));
    }
}

[Trait("Category", "Unit")]
public class GpsReadingTests
{
    private static readonly GeoPosition Somewhere = GeoPosition.Create(-12.1, -77.0);
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 6, 45, 0, TimeSpan.FromHours(-5));

    [Fact]
    public void Create_ValidReading_KeepsOptionalValues()
    {
        var reading = GpsReading.Create(Somewhere, Now, accuracyMeters: 8, speedKmh: 32.5, headingDegrees: 180);

        reading.AccuracyMeters.ShouldBe(8);
        reading.SpeedKmh.ShouldBe(32.5);
        reading.HeadingDegrees.ShouldBe(180);
    }

    [Theory]
    [InlineData(-1, null, null)]
    [InlineData(5, -3.0, null)]
    [InlineData(5, null, 360.0)]
    [InlineData(5, null, -0.5)]
    public void Create_InvalidValues_ThrowsInvalidReading(double accuracy, double? speed, double? heading)
    {
        var ex = Should.Throw<DomainException>(() => GpsReading.Create(Somewhere, Now, accuracy, speed, heading));

        ex.Code.ShouldBe(TrackingErrors.InvalidReading);
    }
}

[Trait("Category", "Unit")]
public class EtaEstimatorTests
{
    [Theory]
    [InlineData(1000, 30.0, 2)]   // 1 km a 30 km/h = 2 min
    [InlineData(850, 20.0, 3)]    // 2.55 min → redondea arriba
    [InlineData(1000, null, 3)]   // sin velocidad: 20 km/h urbano
    [InlineData(1000, 2.0, 3)]    // detenido en semáforo: usa 20 km/h
    [InlineData(0, 30.0, 0)]
    public void EstimateMinutes_ReturnsCeilingMinutes(double meters, double? speed, int expected)
    {
        EtaEstimator.EstimateMinutes(meters, speed).ShouldBe(expected);
    }
}
