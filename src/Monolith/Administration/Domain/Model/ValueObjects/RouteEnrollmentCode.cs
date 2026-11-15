using System.Security.Cryptography;

namespace Monolith.Administration.Domain.Model.ValueObjects;

/// <summary>
///     Code the transport company shares with the families of a route. A parent (Stakeholder bounded context) uses it
///     to enroll a child: routes are never listed publicly, so only who received the code can find the route.
/// </summary>
/// <remarks>8 characters without look-alike symbols (no 0/O, 1/I/L), e.g. <c>K7QM4P2X</c>.</remarks>
public record RouteEnrollmentCode
{
    public const int Length = 8;
    public const int MaxLength = 12;
    private const string Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";

    /// <summary>Parameterless constructor required by EF Core materialization.</summary>
    public RouteEnrollmentCode()
    {
        Value = string.Empty;
    }

    public RouteEnrollmentCode(string value) : this(value, true)
    {
    }

    public RouteEnrollmentCode(string value, bool validate)
    {
        var normalized = Normalize(value);
        if (validate && !IsWellFormed(normalized))
            throw new ArgumentException($"'{value}' is not a valid enrollment code.", nameof(value));
        Value = normalized;
    }

    public string Value { get; init; }

    public static RouteEnrollmentCode Generate()
    {
        Span<char> chars = stackalloc char[Length];
        for (var i = 0; i < Length; i++)
            chars[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        return new RouteEnrollmentCode(new string(chars));
    }

    /// <summary>Upper case, without spaces or dashes (parents type "k7qm-4p2x").</summary>
    public static string Normalize(string? value) =>
        new((value ?? string.Empty).Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    public static bool IsWellFormed(string? value) =>
        value is { Length: >= 6 and <= MaxLength } && value.All(c => Alphabet.Contains(c));

    public override string ToString() => Value;
}
