namespace Monolith.Administration.Domain.Model.ValueObjects;

/// <summary>Identity of a <c>Vehicle</c> inside the Administration bounded context.</summary>
public record VehicleId(Guid Identifier)
{
    /// <summary>Parameterless constructor required by EF Core materialization. Generates a new identifier.</summary>
    public VehicleId() : this(Guid.NewGuid())
    {
    }

    public static VehicleId New() => new(Guid.NewGuid());

    public override string ToString() => Identifier.ToString();
}
