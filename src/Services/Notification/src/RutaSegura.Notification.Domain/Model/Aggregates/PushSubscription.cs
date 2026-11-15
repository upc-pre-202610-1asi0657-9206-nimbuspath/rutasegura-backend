using RutaSegura.BuildingBlocks.Domain;
using RutaSegura.Notification.Domain.Model.ValueObjects;

namespace RutaSegura.Notification.Domain.Model.Aggregates;

/// <summary>Destino push de un usuario. Infrastructure cifra Token; no debe serializarse ni registrarse en logs.</summary>
public sealed class PushSubscription : AggregateRoot<Guid>
{
    public const int MaxTokenLength = 4096;
    private PushSubscription() { }

    public Guid OrganizationId { get; private set; }
    public Guid UserId { get; private set; }
    public Guid InstallationId { get; private set; }
    public string Token { get; private set; } = string.Empty;
    public PushPlatform Platform { get; private set; }
    public bool IsEnabled { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static PushSubscription Register(Guid organizationId, Guid userId, Guid installationId,
        string token, PushPlatform platform, DateTimeOffset now)
    {
        if (organizationId == Guid.Empty || userId == Guid.Empty || installationId == Guid.Empty || now == default)
            throw new DomainException("PUSH_INVALID_SUBSCRIPTION", "Se requiere organizacion, usuario, instalacion y fecha.");
        ValidateToken(token, platform);
        return new PushSubscription
        {
            Id = Guid.CreateVersion7(), OrganizationId = organizationId, UserId = userId,
            InstallationId = installationId, Token = token, Platform = platform, IsEnabled = true,
            CreatedAt = now.ToUniversalTime(), UpdatedAt = now.ToUniversalTime()
        };
    }

    public void Refresh(string token, PushPlatform platform, DateTimeOffset now)
    {
        ValidateToken(token, platform);
        ValidateTime(now);
        Token = token;
        Platform = platform;
        IsEnabled = true;
        UpdatedAt = now.ToUniversalTime();
    }

    public void Disable(DateTimeOffset now)
    {
        ValidateTime(now);
        IsEnabled = false;
        UpdatedAt = now.ToUniversalTime();
    }

    private void ValidateTime(DateTimeOffset now)
    {
        if (now < UpdatedAt)
            throw new DomainException("PUSH_INVALID_UPDATE_TIME", "La actualizacion no puede retroceder en el tiempo.");
    }

    private static void ValidateToken(string token, PushPlatform platform)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > MaxTokenLength || token.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)) || !Enum.IsDefined(platform))
            throw new DomainException("PUSH_INVALID_TOKEN", "Se requiere un token push valido y una plataforma conocida.");
    }
}
