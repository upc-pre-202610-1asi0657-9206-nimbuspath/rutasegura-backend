namespace Monolith.Administration.Domain.Model.ValueObjects;

/// <summary>
///     Reference to a trip owned by the Trip &amp; Tracking service. Administration never creates trips;
///     it only receives their identifiers through integration events.
/// </summary>
public record TripId
{
    /// <summary>Parameterless constructor required by EF Core materialization.</summary>
    public TripId()
    {
        Identifier = Guid.Empty;
    }

    public TripId(Guid identifier)
    {
        if (identifier == Guid.Empty)
            throw new ArgumentException("Trip identifier cannot be empty.", nameof(identifier));
        Identifier = identifier;
    }

    public Guid Identifier { get; init; }

    public override string ToString() => Identifier.ToString();
}
