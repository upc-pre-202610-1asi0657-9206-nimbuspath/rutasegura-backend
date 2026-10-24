namespace Monolith.Administration.Domain.Model.ValueObjects;

/// <summary>Identity of a <c>Route</c> inside the Administration bounded context.</summary>
public record RouteId(Guid Identifier)
{
    /// <summary>Parameterless constructor required by EF Core materialization. Generates a new identifier.</summary>
    public RouteId() : this(Guid.NewGuid())
    {
    }

    public static RouteId New() => new(Guid.NewGuid());

    public override string ToString() => Identifier.ToString();
}
