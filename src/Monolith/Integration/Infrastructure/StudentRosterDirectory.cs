using Monolith.Administration.Application.Internal.OutboundServices;
using Monolith.Stakeholder.Interfaces.ACL;
namespace Monolith.Integration.Infrastructure;
/// <summary>Composition adapter between published contracts; neither bounded context depends on the other's implementation.</summary>
public sealed class StudentRosterDirectory(IStakeholderRosterContextFacade stakeholder) : IStudentRosterDirectory
{
    public Task<bool> BelongsToOperationAsync(Guid organization, Guid student, CancellationToken ct) => stakeholder.BelongsToOperationAsync(organization, student, ct);
}
