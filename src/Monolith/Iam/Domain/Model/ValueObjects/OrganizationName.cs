namespace Monolith.Iam.Domain.Model.ValueObjects;

/// <summary>
///     Value object representing the display name of an <c>Organization</c>.
/// </summary>
public record OrganizationName
{
    public const int MaxLength = 150;

    public OrganizationName() : this(string.Empty, false)
    {
    }

    public OrganizationName(string value) : this(value, true)
    {
    }

    public OrganizationName(string value, bool validate)
    {
        if (validate)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("Organization name cannot be empty.", nameof(value));
            if (value.Length > MaxLength)
                throw new ArgumentException("Organization name is too long.", nameof(value));
        }

        Value = value.Trim();
    }

    public string Value { get; init; } = string.Empty;

    public override string ToString() => Value;
}