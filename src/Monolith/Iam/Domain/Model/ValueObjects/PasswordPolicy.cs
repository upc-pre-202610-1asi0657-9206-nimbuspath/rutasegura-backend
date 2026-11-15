namespace Monolith.Iam.Domain.Model.ValueObjects;

/// <summary>
///     Rules a plain-text password must meet before it is hashed: 8 to 72 characters (BCrypt only uses the first 72
///     bytes), at least one letter and one digit.
/// </summary>
public static class PasswordPolicy
{
    public const int MinLength = 8;
    public const int MaxLength = 72;

    public const string Description =
        "The password must have between 8 and 72 characters and include at least one letter and one digit.";

    public static bool IsSatisfiedBy(string? password) =>
        !string.IsNullOrEmpty(password) &&
        password.Length is >= MinLength and <= MaxLength &&
        password.Any(char.IsLetter) &&
        password.Any(char.IsDigit);
}
