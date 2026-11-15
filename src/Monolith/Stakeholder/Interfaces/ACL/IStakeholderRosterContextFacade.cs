using Monolith.Stakeholder.Application.QueryServices;
namespace Monolith.Stakeholder.Interfaces.ACL;
public interface IStakeholderRosterContextFacade { Task<bool> BelongsToOperationAsync(Guid organization, Guid student, CancellationToken ct); }
public sealed class StakeholderRosterContextFacade(IGuardianAccessQueryService queries) : IStakeholderRosterContextFacade
{
    public async Task<bool> BelongsToOperationAsync(Guid organization, Guid student, CancellationToken ct) =>
        (await queries.RosterAsync(organization, ct)).Any(s => s.Id.Identifier == student);
}
