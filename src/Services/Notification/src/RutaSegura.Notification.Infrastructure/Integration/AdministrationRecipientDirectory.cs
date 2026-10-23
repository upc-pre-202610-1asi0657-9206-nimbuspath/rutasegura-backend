using System.Net.Http.Json;
using System.Net;
using RutaSegura.BuildingBlocks.Application.Results;
using RutaSegura.Notification.Application.Internal.OutboundServices;
using RutaSegura.Notification.Application.Model.Results;

namespace RutaSegura.Notification.Infrastructure.Integration;

/// <summary>Contrato interno a implementar por Administration/Roster; no consulta sus tablas ni inventa destinatarios.</summary>
public sealed class AdministrationRecipientDirectory(HttpClient client) : INotificationRecipientDirectory
{
    public async Task<IReadOnlyList<NotificationRecipient>> ForStudentsAsync(Guid organizationId, IReadOnlyCollection<Guid> studentIds, CancellationToken cancellationToken) =>
        await Recipients("students", new { organizationId, studentIds }, organizationId, cancellationToken);

    public async Task<IReadOnlyList<NotificationRecipient>> OperatorsAsync(Guid organizationId, CancellationToken cancellationToken) =>
        await Recipients("operators", new { organizationId }, organizationId, cancellationToken);

    public async Task<Result<IReadOnlyList<NotificationRecipient>>> ForAnnouncementAsync(Guid organizationId, Guid? routeId, CancellationToken cancellationToken)
    {
        using var response = await client.PostAsJsonAsync("internal/v1/notifications/recipients/announcement", new { organizationId, routeId }, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) return Error.NotFound("ANNOUNCEMENT_ROUTE_NOT_FOUND", "La ruta no esta disponible para este transportista.");
        if (response.StatusCode == HttpStatusCode.Forbidden) return Error.Forbidden("ANNOUNCEMENT_ROUTE_FORBIDDEN", "No se permite acceder a esa ruta.");
        response.EnsureSuccessStatusCode();
        return Result<IReadOnlyList<NotificationRecipient>>.Success(await ReadRecipients(response, organizationId, cancellationToken));
    }

    public async Task<IReadOnlyCollection<Guid>> CurrentStudentLinksAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken)
    {
        using var response = await client.PostAsJsonAsync("internal/v1/notifications/student-links", new { organizationId, userId }, cancellationToken);
        response.EnsureSuccessStatusCode();
        StudentLinksResponse? body;
        try { body = await response.Content.ReadFromJsonAsync<StudentLinksResponse>(cancellationToken: cancellationToken); }
        catch (System.Text.Json.JsonException) { throw new HttpRequestException("NOTIFICATION_DIRECTORY_INVALID_RESPONSE"); }
        if (body is null || body.OrganizationId != organizationId || body.UserId != userId || body.StudentIds is null || body.StudentIds.Any(id => id == Guid.Empty))
            throw new HttpRequestException("NOTIFICATION_DIRECTORY_INVALID_RESPONSE");
        return body.StudentIds;
    }

    private async Task<IReadOnlyList<NotificationRecipient>> Recipients(string operation, object request, Guid organizationId, CancellationToken ct)
    {
        using var response = await client.PostAsJsonAsync("internal/v1/notifications/recipients/" + operation, request, ct);
        response.EnsureSuccessStatusCode();
        return await ReadRecipients(response, organizationId, ct);
    }

    private static async Task<IReadOnlyList<NotificationRecipient>> ReadRecipients(HttpResponseMessage response, Guid organizationId, CancellationToken ct)
    {
        RecipientResponse? body;
        try { body = await response.Content.ReadFromJsonAsync<RecipientResponse>(cancellationToken: ct); }
        catch (System.Text.Json.JsonException) { throw new HttpRequestException("NOTIFICATION_DIRECTORY_INVALID_RESPONSE"); }
        if (body is null || body.OrganizationId != organizationId || body.Recipients is null || body.Recipients.Length > 10000 ||
            body.Recipients.Any(r => r is null || r.OrganizationId != organizationId || r.UserId == Guid.Empty))
            throw new HttpRequestException("NOTIFICATION_DIRECTORY_INVALID_RESPONSE");
        return body.Recipients;
    }

    public sealed record RecipientResponse(Guid OrganizationId, NotificationRecipient[] Recipients);
    public sealed record StudentLinksResponse(Guid OrganizationId, Guid UserId, Guid[] StudentIds);
}
