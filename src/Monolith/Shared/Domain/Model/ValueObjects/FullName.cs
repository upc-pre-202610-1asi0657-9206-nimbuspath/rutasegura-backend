namespace Monolith.Shared.Domain.Model.ValueObjects;

/// <summary>
///     Shared value object for a person's full name.
/// </summary>
/// <param name="FirstName">The first name.</param>
/// <param name="LastName">The last name.</param>
public record FullName
{
    public const int MaxLength = 100;

    public FullName() : this(string.Empty, string.Empty, false)
    {
    }

    public FullName(string firstName, string lastName) : this(firstName, lastName, true)
    {
    }

    public FullName(string firstName, string lastName, bool validate)
    {
        if (validate)
        {
            if (string.IsNullOrWhiteSpace(firstName))
                throw new ArgumentException("First name cannot be empty.", nameof(firstName));
            if (string.IsNullOrWhiteSpace(lastName))
                throw new ArgumentException("Last name cannot be empty.", nameof(lastName));
            if (firstName.Length > MaxLength || lastName.Length > MaxLength)
                throw new ArgumentException("Name values are too long.");
        }

        FirstName = firstName.Trim();
        LastName = lastName.Trim();
    }

    public string FirstName { get; init; } = string.Empty;

    public string LastName { get; init; } = string.Empty;

    public override string ToString() => $"{FirstName} {LastName}".Trim();
}