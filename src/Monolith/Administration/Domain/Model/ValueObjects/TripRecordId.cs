namespace Monolith.Administration.Domain.Model.ValueObjects;

/// <summary>Identity of a <c>TripRecord</c> inside the Administration bounded context.</summary>
public record TripRecordId(Guid Identifier)
{
    /// <summary>Parameterless constructor required by EF Core materialization. Generates a new identifier.</summary>
    public TripRecordId() : this(Guid.NewGuid())
    {
    }

    public static TripRecordId New() => new(Guid.NewGuid());

    public override string ToString() => Identifier.ToString();
}
