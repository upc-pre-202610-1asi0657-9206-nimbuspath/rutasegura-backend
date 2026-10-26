namespace Monolith.Stakeholder.Domain.Model.ValueObjects;

/// <summary>Contact phone of a parent: 7 to 15 digits, optional leading "+" (e.g. 987654321 or +51987654321).</summary>
public record GuardianPhoneNumber
{
    public const int MaxLength = 16;

    public GuardianPhoneNumber() : this(string.Empty, false)
    {
    }

    public GuardianPhoneNumber(string value) : this(value, true)
    {
    }

    public GuardianPhoneNumber(string value, bool validate)
    {
        var normalized = new string((value ?? string.Empty).Where(c => char.IsDigit(c) || c == '+').ToArray());
        if (validate)
        {
            var digits = normalized.TrimStart('+');
            if (digits.Length is < 7 or > 15 || !digits.All(char.IsDigit) || normalized.LastIndexOf('+') > 0)
                throw new ArgumentException("The phone number must have 7 to 15 digits.", nameof(value));
        }

        Value = normalized;
    }

    public string Value { get; init; }

    public override string ToString() => Value;
}
