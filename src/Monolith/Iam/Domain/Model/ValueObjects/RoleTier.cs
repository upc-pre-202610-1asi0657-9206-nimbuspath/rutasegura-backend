namespace Monolith.Iam.Domain.Model.ValueObjects;

/// <summary>
///     Value object representing the access tier granted to a <c>User</c>.
/// </summary>
public record RoleTier
{
    public const string Admin = "ADMIN";
    public const string Driver = "DRIVER";
    public const string Parent = "PARENT";

    private static readonly string[] AllowedValues = [Admin, Driver, Parent];

    public RoleTier() : this(Parent, false)
    {
    }

    public RoleTier(string value) : this(value, true)
    {
    }

    public RoleTier(string value, bool validate)
    {
        var normalized = value.Trim().ToUpperInvariant();
        if (validate && !AllowedValues.Contains(normalized))
            throw new ArgumentException($"'{value}' is not a valid role tier.", nameof(value));
        Value = normalized;
    }

    public string Value { get; init; } = Parent;

    public bool IsAdmin() => Value == Admin;

    public bool IsDriver() => Value == Driver;

    public bool IsParent() => Value == Parent;

    public override string ToString() => Value;
}