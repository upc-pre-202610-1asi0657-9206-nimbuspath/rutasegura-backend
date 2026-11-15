using Monolith.Shared.Application.Model;
using Monolith.Stakeholder.Domain.Model.Aggregates;
using Monolith.Stakeholder.Domain.Model.Commands;
namespace Monolith.Stakeholder.Application.CommandServices;
public interface IGuardianAccessCommandService
{
    Task<Result<OrganizationGuardian>> RegisterGuardianAsync(Guid organization, Guid guardian, string phone, string? document, CancellationToken ct);
    Task<Result<Student>> RegisterStudentAsync(Guid organization, RegisterStudentCommand command, CancellationToken ct);
    Task<Result<GuardianAccessGrant>> ReviewAsync(Guid organization, Guid actor, Guid guardian, Guid student, bool verify, string reference, CancellationToken ct);
}
