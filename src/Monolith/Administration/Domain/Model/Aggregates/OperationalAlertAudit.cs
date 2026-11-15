using Monolith.Shared.Domain.Model.Entities;

namespace Monolith.Administration.Domain.Model.Aggregates;

/// <summary>
///     Audit trait for <see cref="OperationalAlert" />, populated automatically by the
///     <c>AuditableEntityInterceptor</c> on save.
/// </summary>
public partial class OperationalAlert : IAuditableEntity
{
    public DateTimeOffset? CreatedAt { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }
}
