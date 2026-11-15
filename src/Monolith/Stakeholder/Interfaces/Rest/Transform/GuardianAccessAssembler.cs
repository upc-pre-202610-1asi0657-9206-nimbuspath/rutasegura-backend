using Monolith.Stakeholder.Domain.Model.Aggregates;
using Monolith.Stakeholder.Interfaces.Rest.Resources;
namespace Monolith.Stakeholder.Interfaces.Rest.Transform;
public static class GuardianAccessAssembler
{
    public static GuardianAccessResource ToResource(GuardianAccessGrant g) => new(g.OrganizationId, g.GuardianUserId, g.StudentId, g.Status, g.RequestedAt, g.ReviewedBy, g.ReviewedAt, g.VerificationReference, g.Version);
}
