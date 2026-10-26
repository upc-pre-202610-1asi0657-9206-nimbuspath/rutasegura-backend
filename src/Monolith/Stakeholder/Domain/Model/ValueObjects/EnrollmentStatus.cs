namespace Monolith.Stakeholder.Domain.Model.ValueObjects;

/// <summary>
///     ACTIVE: the child rides the route. WITHDRAWN: the parent took the child out. CANCELLED: the transport company
///     ended the enrollment (e.g. unpaid service). Only ACTIVE enrollments place the child on a stop.
/// </summary>
public record EnrollmentStatus
{
    public const string Active = "ACTIVE";
    public const string Withdrawn = "WITHDRAWN";
    public const string Cancelled = "CANCELLED";

    private static readonly string[] AllowedValues = [Active, Withdrawn, Cancelled];

    public EnrollmentStatus() : this(Active, false)
    {
    }

    public EnrollmentStatus(string value) : this(value, true)
    {
    }

    public EnrollmentStatus(string value, bool validate)
    {
        var normalized = (value ?? string.Empty).Trim().ToUpperInvariant();
        if (validate && !AllowedValues.Contains(normalized))
            throw new ArgumentException($"'{value}' is not a valid enrollment status.", nameof(value));
        Value = normalized;
    }

    public string Value { get; init; } = Active;

    public static EnrollmentStatus CreateActive() => new(Active);

    public static EnrollmentStatus CreateWithdrawn() => new(Withdrawn);

    public static EnrollmentStatus CreateCancelled() => new(Cancelled);

    public bool IsActive() => Value == Active;

    public override string ToString() => Value;
}
