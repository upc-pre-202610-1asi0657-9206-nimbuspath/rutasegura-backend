namespace Monolith.Iam.Infrastructure.Tokens.Jwt.Configuration;

public class TokenSettings
{
    public required string Secret { get; set; }

    public int ExpirationDays { get; set; } = 7;
}