namespace Monolith.Administration.Application.Internal.OutboundServices;
public interface IStudentRosterDirectory { Task<bool> BelongsToOperationAsync(Guid organization, Guid student, CancellationToken ct); }
