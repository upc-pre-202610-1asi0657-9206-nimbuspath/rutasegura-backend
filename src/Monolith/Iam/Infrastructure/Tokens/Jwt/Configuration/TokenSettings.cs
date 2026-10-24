namespace Monolith.Iam.Infrastructure.Tokens.Jwt.Configuration;

/// <summary>
///     JWT settings (section <c>TokenSettings</c>). The secret is never versioned: it comes from
///     <c>appsettings.Development.json</c> on a developer machine and from the environment
///     (<c>TokenSettings__Secret</c>) or a secret store in any deployed environment.
/// </summary>
public class TokenSettings
{
    public const int MinimumSecretLength = 32;

    public required string Secret { get; set; }

    /// <summary>Lifetime of an access token. Short on purpose: there is no refresh token or revocation list yet.</summary>
    public int ExpirationHours { get; set; } = 8;

    /// <summary>Written as <c>iss</c> and validated when set (Trip &amp; Tracking validates it when configured too).</summary>
    public string? Issuer { get; set; }

    /// <summary>Written as <c>aud</c> and validated when set.</summary>
    public string? Audience { get; set; }
}
