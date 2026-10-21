using Monolith.Shared.Domain.Model.Aggregates;

namespace Monolith.Iam.Application.Internal.OutboundServices;

public interface ITokenService
{
    string GenerateToken(User user);
}