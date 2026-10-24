namespace Monolith.Administration.Domain.Model.ValueObjects;

/// <summary>Identity of a <c>RouteStop</c> inside the Administration bounded context.</summary>
public record RouteStopId(Guid Identifier)
{
    /// <summary>Parameterless constructor required by EF Core materialization. Generates a new identifier.</summary>
    public RouteStopId() : this(Guid.NewGuid())
    {
    }

    public static RouteStopId New() => new(Guid.NewGuid());

    public override string ToString() => Identifier.ToString();
}
