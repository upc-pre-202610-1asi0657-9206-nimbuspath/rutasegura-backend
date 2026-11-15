using System.Net;
using RutaSegura.Monolith.Administration.IntegrationTests.Support;

namespace RutaSegura.Monolith.Administration.IntegrationTests;

/// <summary>Swagger documents the Administration endpoints (and generates without schema conflicts).</summary>
public sealed class OpenApiTests : IDisposable
{
    private readonly MonolithApp _app = new(settings: new Dictionary<string, string?> { ["Swagger:Enabled"] = "true" });

    public void Dispose() => _app.Dispose();

    [Fact]
    public async Task SwaggerDocument_ListsTheAdministrationEndpointsAndTheBearerScheme()
    {
        var document = await _app.CreateClient().Get("/swagger/v1/swagger.json").ExpectAsync(HttpStatusCode.OK);

        var paths = document.GetProperty("paths").EnumerateObject().Select(p => p.Name).ToList();
        paths.ShouldContain("/api/v1/roster/drivers");
        paths.ShouldContain("/api/v1/roster/drivers/me");
        paths.ShouldContain("/api/v1/roster/vehicles/{vehicleId}/disable");
        paths.ShouldContain("/api/v1/routes/{routeId}/stops/{stopId}/students/{studentId}");
        paths.ShouldContain("/api/v1/routes/{routeId}/activate");
        paths.ShouldContain("/api/v1/routes/quota");
        paths.ShouldContain("/api/v1/operations/alerts/{alertId}/acknowledge");
        document.GetProperty("components").GetProperty("securitySchemes").TryGetProperty("Bearer", out _)
            .ShouldBeTrue();
    }
}
