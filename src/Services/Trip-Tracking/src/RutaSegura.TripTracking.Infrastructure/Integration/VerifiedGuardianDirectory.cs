using System.Net.Http.Json;
using RutaSegura.TripTracking.Application.Abstractions;
using RutaSegura.TripTracking.Domain.Common;
namespace RutaSegura.TripTracking.Infrastructure.Integration;
/// <summary>Authoritative scoped authorization. The AMQP projection is not a permission cache.</summary>
public sealed class VerifiedGuardianDirectory(HttpClient client) : IGuardianDirectory
{
    public Task<IReadOnlyCollection<StudentId>> GetStudentsOfGuardianAsync(GuardianId guardianId, CancellationToken ct) => Read(guardianId, null, ct);
    public Task<IReadOnlyCollection<StudentId>> GetStudentsForOrganizationAsync(GuardianId guardianId, Guid organizationId, CancellationToken ct) => Read(guardianId, organizationId, ct);
    private async Task<IReadOnlyCollection<StudentId>> Read(GuardianId guardian, Guid? organization, CancellationToken ct)
    {
        try
        {
            using var response = await client.PostAsJsonAsync("internal/v1/notifications/guardian-students", new { userId = guardian.Value, organizationId = organization }, ct);
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<DirectoryResponse>(cancellationToken: ct);
            if (result is null || result.UserId != guardian.Value || result.OrganizationId != organization || result.StudentIds is null || result.StudentIds.Length > 10000 || result.StudentIds.Any(id => id == Guid.Empty))
                throw new GuardianDirectoryUnavailableException();
            return result.StudentIds.Distinct().Select(id => new StudentId(id)).ToArray();
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new GuardianDirectoryUnavailableException(); }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException or InvalidOperationException)
        { throw new GuardianDirectoryUnavailableException(); }
    }
    private sealed record DirectoryResponse(Guid UserId, Guid? OrganizationId, Guid[] StudentIds);
}
