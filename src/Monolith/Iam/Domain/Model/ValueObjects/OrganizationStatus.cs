namespace Monolith.Iam.Domain.Model.ValueObjects;

/// <summary>
///     Value object representing the lifecycle status of an <c>Organization</c>.
/// </summary>
public record OrganizationStatus
{
    public const string Active = "ACTIVE";
    public const string Suspended = "SUSPENDED";

    private static readonly string[] AllowedValues = [Active, Suspended];

    public OrganizationStatus() : this(Active, false)
    {
    }

    public OrganizationStatus(string value) : this(value, true)
    {
    }

    public OrganizationStatus(string value, bool validate)
    {
        var normalized = value.Trim().ToUpperInvariant();
        if (validate && !AllowedValues.Contains(normalized))
            throw new ArgumentException($"'{value}' is not a valid organization status.", nameof(value));
        Value = normalized;
    }

    public string Value { get; init; } = Active;

    public static OrganizationStatus CreateActive() => new(Active);

    public static OrganizationStatus CreateSuspended() => new(Suspended);

    public bool IsActive() => Value == Active;

    public bool IsSuspended() => Value == Suspended;

    public override string ToString() => Value;
}