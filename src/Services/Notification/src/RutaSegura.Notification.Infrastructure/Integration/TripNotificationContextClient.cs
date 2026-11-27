using System.Net.Http.Json;
using RutaSegura.Notification.Application.Internal.OutboundServices;

namespace RutaSegura.Notification.Infrastructure.Integration;

public sealed class TripNotificationContextClient(HttpClient client) : ITripNotificationContext
{
    public async Task<Guid?> DriverUserIdAsync(Guid organizationId, Guid tripId, CancellationToken ct)
    {
        using var response = await client.GetAsync($"internal/v1/trips/{tripId:D}/notification-context?organizationId={organizationId:D}", ct);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<ContextResponse>(cancellationToken: ct);
        if (body is null || body.OrganizationId != organizationId || body.TripId != tripId)
            throw new HttpRequestException("NOTIFICATION_TRIP_CONTEXT_INVALID_RESPONSE");
        if (body.DriverUserId == Guid.Empty || body.DriverUserId is null)
            throw new HttpRequestException("NOTIFICATION_TRIP_DRIVER_REQUIRED");
        return body.DriverUserId;
    }
    public async Task<bool> IsActiveAsync(Guid organizationId, Guid tripId, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync($"internal/v1/trips/{tripId:D}/notification-context?organizationId={organizationId:D}", cancellationToken);
        response.EnsureSuccessStatusCode();
        ContextResponse? body;
        try { body = await response.Content.ReadFromJsonAsync<ContextResponse>(cancellationToken: cancellationToken); }
        catch (System.Text.Json.JsonException) { throw new HttpRequestException("NOTIFICATION_TRIP_CONTEXT_INVALID_RESPONSE"); }
        if (body is null || body.OrganizationId != organizationId || body.TripId != tripId || body.IsActive is null)
            throw new HttpRequestException("NOTIFICATION_TRIP_CONTEXT_INVALID_RESPONSE");
        return body.IsActive.Value;
    }

    public sealed record ContextResponse(Guid OrganizationId, Guid TripId, bool? IsActive, Guid? DriverUserId = null);
}
