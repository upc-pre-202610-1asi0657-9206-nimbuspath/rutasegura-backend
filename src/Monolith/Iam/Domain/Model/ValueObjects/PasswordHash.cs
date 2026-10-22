namespace Monolith.Iam.Domain.Model.ValueObjects;

/// <summary>
///     Value object wrapping an already-hashed password. Hashing and verification are
///     performed by the application-layer <c>IHashingService</c>; this type only guards
///     against persisting an empty hash.
/// </summary>
public record PasswordHash
{
    public PasswordHash() : this(string.Empty, false)
    {
    }

    public PasswordHash(string value) : this(value, true)
    {
    }

    public PasswordHash(string value, bool validate)
    {
        if (validate && string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Password hash cannot be empty.", nameof(value));
        Value = value;
    }

    public string Value { get; init; } = string.Empty;

    public override string ToString() => Value;
}