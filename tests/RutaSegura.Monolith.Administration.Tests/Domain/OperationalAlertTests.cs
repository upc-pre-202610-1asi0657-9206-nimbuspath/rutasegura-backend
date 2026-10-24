using Monolith.Administration.Domain.Model;
using Monolith.Administration.Domain.Model.Aggregates;
using Monolith.Administration.Domain.Model.Exceptions;
using Monolith.Administration.Domain.Model.ValueObjects;
using Monolith.Shared.Domain.Model.ValueObjects;
using static RutaSegura.Monolith.Administration.Tests.Support.AdministrationMother;

namespace RutaSegura.Monolith.Administration.Tests.Domain;

/// <summary>Follow-up of IncidentReported and SosRaised events coming from Trip &amp; Tracking.</summary>
public sealed class OperationalAlertTests
{
    private static readonly UserId Administrator = new(Guid.NewGuid());

    private static OperationalAlert Incident(string type = "HeavyTraffic", string? detail = null) =>
        OperationalAlert.FromIncident(Organization, new TripId(Guid.NewGuid()), RouteId.New(), "Ruta 3",
            Guid.NewGuid(), IncidentCategory.FromTripTrackingType(type), detail, null, 3, Now);

    private static OperationalAlert Sos() =>
        OperationalAlert.FromSos(Organization, new TripId(Guid.NewGuid()), RouteId.New(), "Ruta 3", Guid.NewGuid(),
            Guid.NewGuid(), null, 4, Now);

    [Theory]
    [InlineData("Accident", "ACCIDENT", "CRITICAL")]
    [InlineData("VehicleFailure", "VEHICLE_FAILURE", "HIGH")]
    [InlineData("StudentUnwell", "STUDENT_UNWELL", "HIGH")]
    [InlineData("RouteDeviation", "ROUTE_DEVIATION", "MEDIUM")]
    [InlineData("HeavyTraffic", "HEAVY_TRAFFIC", "LOW")]
    [InlineData("SomethingNew", "OTHER", "MEDIUM")]
    public void FromIncident_TripTrackingType_IsTranslatedWithItsSeverity(string type, string category,
        string severity)
    {
        var alert = Incident(type);

        alert.Type.IsIncident().ShouldBeTrue();
        alert.Category!.Value.ShouldBe(category);
        alert.Severity.Value.ShouldBe(severity);
        alert.Status.IsOpen().ShouldBeTrue();
    }

    [Fact]
    public void FromIncident_LongDetail_IsTruncated() =>
        Incident(detail: new string('x', 800)).Detail!.Length.ShouldBe(OperationalAlert.DetailMaxLength);

    [Fact]
    public void FromSos_WithoutGps_IsCriticalAndOpen()
    {
        var alert = Sos();

        alert.Type.IsSos().ShouldBeTrue();
        alert.Severity.IsCritical().ShouldBeTrue();
        alert.Location.ShouldBeNull();
        alert.AffectedStudents.ShouldBe(4);
        alert.IsOpen().ShouldBeTrue();
    }

    [Fact]
    public void Acknowledge_OpenAlert_RecordsWhoAndWhen()
    {
        var alert = Sos();

        alert.Acknowledge(Administrator, Now);

        alert.Status.IsAcknowledged().ShouldBeTrue();
        alert.AcknowledgedBy.ShouldBe(Administrator);
        alert.AcknowledgedAt.ShouldBe(Now);
        alert.IsOpen().ShouldBeTrue();
        Should.Throw<AdministrationDomainException>(() => alert.Acknowledge(Administrator, Now))
            .Error.ShouldBe(AdministrationError.AlertInvalidTransition);
    }

    [Fact]
    public void ResolveByAdministrator_Sos_IsRejectedBecauseTripTrackingOwnsIt() =>
        Should.Throw<AdministrationDomainException>(() => Sos().ResolveByAdministrator(Administrator, Now))
            .Error.ShouldBe(AdministrationError.AlertInvalidTransition);

    [Fact]
    public void ResolveFromTripTracking_Sos_ClosesIt()
    {
        var alert = Sos();
        var resolver = Guid.NewGuid();

        alert.ResolveFromTripTracking(resolver, Now.AddMinutes(12));

        alert.IsOpen().ShouldBeFalse();
        alert.ResolvedBy.ShouldBe(resolver);
        alert.ResolvedAt.ShouldBe(Now.AddMinutes(12));
    }

    [Fact]
    public void ResolveFromTripTracking_Incident_IsRejected() =>
        Should.Throw<AdministrationDomainException>(() => Incident().ResolveFromTripTracking(Guid.NewGuid(), Now))
            .Error.ShouldBe(AdministrationError.AlertInvalidTransition);

    [Fact]
    public void ResolveByAdministrator_IncidentTwice_SecondTimeIsRejected()
    {
        var alert = Incident();
        alert.ResolveByAdministrator(Administrator, Now);

        Should.Throw<AdministrationDomainException>(() => alert.ResolveByAdministrator(Administrator, Now))
            .Error.ShouldBe(AdministrationError.AlertInvalidTransition);
    }
}
