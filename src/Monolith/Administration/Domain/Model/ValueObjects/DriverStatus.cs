namespace Monolith.Administration.Domain.Model.ValueObjects;

/// <summary>
///     Lifecycle of a driver profile: PENDING_VERIFICATION → VERIFIED ⇄ SUSPENDED (US-01).
/// </summary>
public record DriverStatus
{
    public const string PendingVerification = "PENDING_VERIFICATION";
    public const string Verified = "VERIFIED";
    public const string Suspended = "SUSPENDED";

    private static readonly string[] AllowedValues = [PendingVerification, Verified, Suspended];

    public DriverStatus() : this(PendingVerification, false)
    {
    }

    public DriverStatus(string value) : this(value, true)
    {
    }

    public DriverStatus(string value, bool validate)
    {
        var normalized = (value ?? string.Empty).Trim().ToUpperInvariant();
        if (validate && !AllowedValues.Contains(normalized))
            throw new ArgumentException($"'{value}' is not a valid driver status.", nameof(value));
        Value = normalized;
    }

    public string Value { get; init; } = PendingVerification;

    public static DriverStatus CreatePendingVerification() => new(PendingVerification);

    public static DriverStatus CreateVerified() => new(Verified);

    public static DriverStatus CreateSuspended() => new(Suspended);

    public bool IsPendingVerification() => Value == PendingVerification;

    public bool IsVerified() => Value == Verified;

    public bool IsSuspended() => Value == Suspended;

    public override string ToString() => Value;
}
