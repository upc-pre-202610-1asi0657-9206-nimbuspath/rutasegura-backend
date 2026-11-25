using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using RutaSegura.TripTracking.Application.Trips.Queries;

namespace RutaSegura.TripTracking.Infrastructure.Integration;

public sealed class TripOperationsDirectory(HttpClient client) : ITripOperationsDirectory
{
    public async Task<TripOperationalDetails> GetAsync(Guid organizationId, Guid routeId,
        IReadOnlyList<Guid> studentIds, IReadOnlyList<TripOperationalStop> stops, CancellationToken ct)
    {
        try
        {
            using var response = await client.PostAsJsonAsync("internal/v1/trips/operational-details",
                new { organizationId, routeId, studentIds, stops }, ct);
            if (response.StatusCode == HttpStatusCode.NotFound) return new([], null);
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<DirectoryResponse>(cancellationToken: ct);
            if (result is null || result.OrganizationId != organizationId || result.RouteId != routeId ||
                result.Students is null || result.Students.Any(student => student is null || !studentIds.Contains(student.StudentId)))
                throw new TripOperationsDirectoryUnavailableException();
            return new(result.Students, result.Itinerary);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new TripOperationsDirectoryUnavailableException(); }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException)
        { throw new TripOperationsDirectoryUnavailableException(); }
    }

    private sealed record DirectoryResponse(Guid OrganizationId, Guid RouteId, TripRosterStudent[] Students, TripItinerary? Itinerary);
}
