using RutaSegura.TripTracking.Application.Abstractions;
using RutaSegura.TripTracking.Application.Common;
using RutaSegura.TripTracking.Application.Tests.Fakes;
using RutaSegura.TripTracking.Application.Trips.Queries;
using RutaSegura.TripTracking.Domain.Common;
namespace RutaSegura.TripTracking.Application.Tests.Trips;
public sealed class VerifiedGuardianScopingTests : TripTrackingTestContext
{
    private sealed class ScopedDirectory(Guid allowedOrganization, Guid student) : IGuardianDirectory
    {
        public bool Revoked;
        public Task<IReadOnlyCollection<StudentId>> GetStudentsOfGuardianAsync(GuardianId guardian, CancellationToken ct) => Task.FromResult<IReadOnlyCollection<StudentId>>([new(student)]);
        public Task<IReadOnlyCollection<StudentId>> GetStudentsForOrganizationAsync(GuardianId guardian, Guid organization, CancellationToken ct) =>
            Task.FromResult<IReadOnlyCollection<StudentId>>(!Revoked && organization == allowedOrganization ? [new(student)] : []);
    }
    [Fact]
    public async Task AGrantInOneOperationDoesNotAuthorizeTheSameStudentInAnotherOperation()
    {
        var allowed = SeedStartedTrip(organizationId: SantaRosa);
        var foreign = SeedStartedTrip(organizationId: OtherCompany);
        var grants = new ScopedDirectory(SantaRosa, Valeria);
        var requester = new Requester(GuardianOfValeria, RequesterRole.Guardian);
        var handler = new GetTripLiveStatusQueryHandler(Trips, grants, Clock);
        (await handler.HandleAsync(new(allowed.Id.Value, requester), Ct)).IsSuccess.ShouldBeTrue();
        (await handler.HandleAsync(new(foreign.Id.Value, requester), Ct)).IsFailure.ShouldBeTrue();
        var list = await new GetGuardianTripsQueryHandler(Trips, grants).HandleAsync(new(GuardianOfValeria, At(6, 0), At(9, 0)), Ct);
        list.Value.ShouldHaveSingleItem().TripId.ShouldBe(allowed.Id.Value);
        grants.Revoked = true;
        (await handler.HandleAsync(new(allowed.Id.Value, requester), Ct)).IsFailure.ShouldBeTrue();
        (await new GetTripPositionQueryHandler(Trips, grants, LastPositions, Clock).HandleAsync(new(allowed.Id.Value, requester), Ct)).IsFailure.ShouldBeTrue();
    }
}
