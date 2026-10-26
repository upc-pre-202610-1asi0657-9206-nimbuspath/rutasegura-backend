using Monolith.Shared.Application.Model;
using Monolith.Stakeholder.Domain.Model.Queries;

namespace Monolith.Stakeholder.Application.QueryServices;

public interface IStakeholderQueryService
{
    Task<IReadOnlyList<StudentDetails>> Handle(GetMyStudentsQuery query, CancellationToken cancellationToken);

    Task<StudentDetails?> Handle(GetMyStudentQuery query, CancellationToken cancellationToken);

    Task<GuardianProfileDetails> Handle(GetGuardianProfileQuery query, CancellationToken cancellationToken);

    Task<RouteOffer?> Handle(GetRouteOfferQuery query, CancellationToken cancellationToken);

    Task<IReadOnlyList<RouteStudentDetails>> Handle(GetOrganizationEnrollmentsQuery query,
        CancellationToken cancellationToken);

    /// <summary>Fails with RouteNotFound / RouteNotOperatedByDriver when the requester may not see the route.</summary>
    Task<Result<IReadOnlyList<RouteStudentDetails>>> Handle(GetRouteStudentsQuery query,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<RouteStudentDetails>> Handle(GetDriverStudentsQuery query, CancellationToken cancellationToken);

    /// <summary>Fails with StudentNotFound / StudentPhotoNotFound / StudentAccessDenied.</summary>
    Task<Result<StudentPhotoContent>> Handle(GetStudentPhotoQuery query, CancellationToken cancellationToken);
}
