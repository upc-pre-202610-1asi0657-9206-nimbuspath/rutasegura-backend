namespace Monolith.Stakeholder.Domain.Model.ValueObjects;

/// <summary>
///     Identity of a child. It is the same identifier Route Planning keeps on its stops and Trip &amp; Tracking on its
///     passengers, so every context speaks about the same student.
/// </summary>
public record StudentId
{
    /// <summary>Parameterless constructor required by EF Core materialization.</summary>
    public StudentId()
    {
        Identifier = Guid.Empty;
    }

    public StudentId(Guid identifier)
    {
        if (identifier == Guid.Empty)
            throw new ArgumentException("Student identifier cannot be empty.", nameof(identifier));
        Identifier = identifier;
    }

    public Guid Identifier { get; init; }

    public static StudentId New() => new(Guid.NewGuid());

    public override string ToString() => Identifier.ToString();
}
