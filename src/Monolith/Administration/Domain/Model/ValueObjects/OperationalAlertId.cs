namespace Monolith.Administration.Domain.Model.ValueObjects;

/// <summary>Identity of a <c>OperationalAlert</c> inside the Administration bounded context.</summary>
public record OperationalAlertId(Guid Identifier)
{
    /// <summary>Parameterless constructor required by EF Core materialization. Generates a new identifier.</summary>
    public OperationalAlertId() : this(Guid.NewGuid())
    {
    }

    public static OperationalAlertId New() => new(Guid.NewGuid());

    public override string ToString() => Identifier.ToString();
}
