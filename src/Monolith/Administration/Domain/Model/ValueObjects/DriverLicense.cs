using System.Text.RegularExpressions;

namespace Monolith.Administration.Domain.Model.ValueObjects;

/// <summary>
///     Driving licence of a driver (Peruvian classes A-I to A-IIIc). A driver can only be assigned to a route
///     while the licence is valid.
/// </summary>
public record DriverLicense
{
    private static readonly string[] AllowedCategories = ["A-I", "A-IIA", "A-IIB", "A-IIIA", "A-IIIB", "A-IIIC"];

    /// <summary>Parameterless constructor required by EF Core materialization.</summary>
    public DriverLicense()
    {
        Number = string.Empty;
        Category = AllowedCategories[0];
        ExpiresOn = DateOnly.MinValue;
    }

    public DriverLicense(string number, string category, DateOnly expiresOn)
    {
        var normalizedNumber = (number ?? string.Empty).Trim().ToUpperInvariant();
        if (!Regex.IsMatch(normalizedNumber, "^[A-Z0-9]{6,12}$"))
            throw new ArgumentException("Driver license number must have between 6 and 12 letters or digits.", nameof(number));

        var normalizedCategory = (category ?? string.Empty).Trim().ToUpperInvariant();
        if (!AllowedCategories.Contains(normalizedCategory))
            throw new ArgumentException($"'{category}' is not a valid driver license category.", nameof(category));

        Number = normalizedNumber;
        Category = normalizedCategory;
        ExpiresOn = expiresOn;
    }

    public string Number { get; init; }

    public string Category { get; init; }

    public DateOnly ExpiresOn { get; init; }

    /// <summary>The licence is valid up to and including its expiration date.</summary>
    public bool IsValidOn(DateOnly date) => date <= ExpiresOn;

    public override string ToString() => $"{Category} {Number}";
}
