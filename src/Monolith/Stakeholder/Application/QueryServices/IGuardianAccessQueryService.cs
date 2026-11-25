using Monolith.Stakeholder.Domain.Model.Aggregates;
namespace Monolith.Stakeholder.Application.QueryServices;
public sealed record AuthorizedGuardianStudent(Guid OrganizationId, Guid UserId, Guid StudentId);
public sealed record OperationGuardianDetails(Guid OrganizationId, Guid UserId, string? FullName, string? Email, string? PhoneNumber, string? DocumentNumber);
public interface IGuardianAccessQueryService
{
    Task<IReadOnlyList<Domain.Model.Aggregates.Student>> RosterAsync(Guid organization, IReadOnlyCollection<Guid> studentIds, CancellationToken ct);
    Task<IReadOnlyList<GuardianAccessGrant>> ForGuardianAsync(Guid guardian, CancellationToken ct);
    Task<IReadOnlyList<Student>> RosterAsync(Guid organization, CancellationToken ct);
    Task<IReadOnlyList<GuardianAccessGrant>> ListAsync(Guid organization, CancellationToken ct);
    Task<IReadOnlyList<OperationGuardianDetails>> GuardiansAsync(Guid organization, CancellationToken ct);
    Task<IReadOnlyList<AuthorizedGuardianStudent>> CurrentAsync(Guid? organization, Guid? guardian, IReadOnlyCollection<Guid>? students, Guid? route, CancellationToken ct, bool requireActiveEnrollment = true);
    Task<IReadOnlyList<Guid>> OperatorsAsync(Guid organization, CancellationToken ct);
    Task<bool> OwnsRouteAsync(Guid organization, Guid route, CancellationToken ct);
}
