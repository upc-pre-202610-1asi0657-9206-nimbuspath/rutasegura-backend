namespace Monolith.Stakeholder.Domain.Model.ValueObjects;

public record EnrollmentId
{
    /// <summary>Parameterless constructor required by EF Core materialization.</summary>
    public EnrollmentId()
    {
        Identifier = Guid.Empty;
    }

    public EnrollmentId(Guid identifier)
    {
        if (identifier == Guid.Empty)
            throw new ArgumentException("Enrollment identifier cannot be empty.", nameof(identifier));
        Identifier = identifier;
    }

    public Guid Identifier { get; init; }

    public static EnrollmentId New() => new(Guid.NewGuid());

    public override string ToString() => Identifier.ToString();
}
