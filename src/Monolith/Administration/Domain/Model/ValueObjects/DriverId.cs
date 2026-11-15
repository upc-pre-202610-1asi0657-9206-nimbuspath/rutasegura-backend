namespace Monolith.Administration.Domain.Model.ValueObjects;

/// <summary>Identity of a <c>Driver</c> inside the Administration bounded context.</summary>
public record DriverId(Guid Identifier)
{
    /// <summary>Parameterless constructor required by EF Core materialization. Generates a new identifier.</summary>
    public DriverId() : this(Guid.NewGuid())
    {
    }

    public static DriverId New() => new(Guid.NewGuid());

    public override string ToString() => Identifier.ToString();
}
