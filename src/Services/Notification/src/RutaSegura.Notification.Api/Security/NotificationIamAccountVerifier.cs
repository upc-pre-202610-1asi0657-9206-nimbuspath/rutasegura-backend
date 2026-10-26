using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace RutaSegura.Notification.Api.Security;

public sealed class NotificationIamOptions
{
    public string BaseUrl { get; set; } = "";
    public bool AllowInsecureHttp { get; set; }
    public AccountValidationOptions AccountValidation { get; set; } = new();

    public sealed class AccountValidationOptions
    {
        public bool Enabled { get; set; } = true;
        public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(3);
    }

    public Uri Validate()
    {
        if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && !(AllowInsecureHttp && uri.Scheme == Uri.UriSchemeHttp)) ||
            uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0)
            throw new InvalidOperationException("Configura Iam:BaseUrl HTTPS; HTTP local requiere Iam:AllowInsecureHttp=true.");
        if (AccountValidation.Timeout <= TimeSpan.Zero || AccountValidation.Timeout > TimeSpan.FromSeconds(30))
            throw new InvalidOperationException("Iam:AccountValidation:Timeout debe ser mayor que cero y no superar 30 segundos.");
        return uri;
    }
}

public enum NotificationIamAccountState { Active, Rejected, Unavailable }

public interface INotificationIamAccountVerifier
{
    Task<NotificationIamAccountState> VerifyAsync(string token, Guid userId, Guid organizationId, string role, CancellationToken ct);
}

/// <summary>ACL HTTP: usa el JWT original y el contrato publico de IAM, sin leer sus tablas ni guardar datos personales.</summary>
public sealed class NotificationIamAccountVerifier(HttpClient client) : INotificationIamAccountVerifier
{
    public const string ClientName = "notification-iam";

    public async Task<NotificationIamAccountState> VerifyAsync(string token, Guid userId, Guid organizationId, string role, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/users/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        try
        {
            // ResponseContentRead respeta MaxResponseContentBufferSize tambien ante una respuesta malformada.
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseContentRead, ct);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
                return NotificationIamAccountState.Rejected;
            if (response.StatusCode != HttpStatusCode.OK) return NotificationIamAccountState.Unavailable;
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var account = json.RootElement;
            return account.ValueKind == JsonValueKind.Object &&
                account.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String && id.TryGetGuid(out var actualUser) && actualUser == userId &&
                account.TryGetProperty("organizationId", out var org) && org.ValueKind == JsonValueKind.String && org.TryGetGuid(out var actualOrg) && actualOrg == organizationId &&
                account.TryGetProperty("roleTier", out var tier) && tier.ValueKind == JsonValueKind.String && tier.GetString() == role
                ? NotificationIamAccountState.Active : NotificationIamAccountState.Rejected;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return NotificationIamAccountState.Unavailable; }
        catch (HttpRequestException) { return NotificationIamAccountState.Unavailable; }
        catch (JsonException) { return NotificationIamAccountState.Unavailable; }
    }
}
