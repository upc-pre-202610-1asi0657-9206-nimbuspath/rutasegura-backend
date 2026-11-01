using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RutaSegura.Notification.Application.Internal.QueryServices;

public sealed record NotificationPagePosition(Guid OrganizationId, Guid UserId, bool UnreadOnly,
    DateTimeOffset Snapshot, DateTimeOffset ExpiresAt, DateTimeOffset? BeforeCreatedAt = null, Guid? AfterId = null);

/// <summary>El cursor no concede permisos. Firma y vincula una posicion al usuario, tenant, filtro y vencimiento.</summary>
public sealed class NotificationCursorCodec(byte[] key, TimeProvider clock)
{
    private static string Encode(byte[] data) => Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static byte[] Decode(string value) => Convert.FromBase64String(value.Replace('-', '+').Replace('_', '/').PadRight((value.Length + 3) / 4 * 4, '='));

    public NotificationPagePosition Read(string? token, Guid organization, Guid user, bool unread)
    {
        var now = clock.GetUtcNow();
        if (token is null) return new(organization, user, unread, now, now.AddMinutes(30));
        try
        {
            if (token.Length is < 10 or > 1600) throw new FormatException();
            var parts = token.Split('.');
            if (parts.Length != 2) throw new FormatException();
            var payload = Decode(parts[0]);
            if (!CryptographicOperations.FixedTimeEquals(HMACSHA256.HashData(key, payload), Decode(parts[1]))) throw new FormatException();
            var value = JsonSerializer.Deserialize<NotificationPagePosition>(payload) ?? throw new FormatException();
            if (value.OrganizationId != organization || value.UserId != user || value.UnreadOnly != unread || value.ExpiresAt <= now ||
                value.Snapshot > now.AddSeconds(30) || value.ExpiresAt > value.Snapshot.AddMinutes(30) ||
                (value.BeforeCreatedAt is null) != (value.AfterId is null) || value.AfterId == Guid.Empty || value.BeforeCreatedAt > value.Snapshot)
                throw new FormatException();
            return value;
        }
        catch (Exception ex) when (ex is FormatException or JsonException or ArgumentException) { throw new FormatException("NOTIFICATION_INVALID_CURSOR"); }
    }

    public string Write(NotificationPagePosition value)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(value);
        return Encode(payload) + "." + Encode(HMACSHA256.HashData(key, payload));
    }
}
