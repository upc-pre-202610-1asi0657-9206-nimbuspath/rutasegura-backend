namespace Monolith.Administration.Domain.Model.ValueObjects;

/// <summary>
///     Reference to a student (child) of the operation. The student registry itself (US-03) is not part
///     of this increment, so Route Planning only keeps the stable identifier of the student.
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

    public override string ToString() => Identifier.ToString();
}
