namespace Monolith.Shared.Domain.Model.ValueObjects;

/// <summary>
///     Shared value object that identifies an Organization across bounded contexts.
/// </summary>
/// <remarks>
///     This identifier is owned by the IAM/Organization context but is referenced from
///     <c>Trip</c> and <c>Fleet</c>. It lives in the Shared bounded context so both
///     contexts can reference an organization without coupling to its internal model.
/// </remarks>
/// <param name="Identifier">The unique identifier of the organization.</param>
public record OrganizationId(Guid Identifier)
{
    /// <summary>
    ///     Parameterless constructor required by EF Core materialization and serializers.
    ///     Generates a new identifier.
    /// </summary>
    public OrganizationId() : this(Guid.NewGuid())
    {
    }

    /// <summary>Factory method that creates a brand new <see cref="OrganizationId" />.</summary>
    public static OrganizationId New() => new(Guid.NewGuid());

    /// <inheritdoc />
    public override string ToString() => Identifier.ToString();
}