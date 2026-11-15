using Monolith.Shared.Domain.Model.Entities;

namespace Monolith.Stakeholder.Domain.Model.Aggregates;

/// <summary>Audit trait of <see cref="Student" />, filled by the <c>AuditableEntityInterceptor</c>.</summary>
public partial class Student : IAuditableEntity
{
    public DateTimeOffset? CreatedAt { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }
}
