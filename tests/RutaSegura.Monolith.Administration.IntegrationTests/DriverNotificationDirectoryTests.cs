using System.Net;
using System.Net.Http.Json;
using RutaSegura.Monolith.Administration.IntegrationTests.Support;

namespace RutaSegura.Monolith.Administration.IntegrationTests;

public sealed class DriverNotificationDirectoryTests
{
    [Fact]
    public async Task SuspensionRemovesDriverAndAnIamAccountAloneDoesNotGrantRecipientAccess()
    {
        await using var app = App();
        using var admin = await app.AdminAsync();
        using var service = app.CreateClient();
        service.DefaultRequestHeaders.Add("X-Service-Token", ServiceToken);
        var driver = await admin.RegisterVerifiedDriverAsync(SeedAccounts.Driver);
        await admin.PostJson($"/api/v1/roster/drivers/{driver}/suspend").ExpectAsync(HttpStatusCode.OK);
        foreach (var userId in new[] { SeedAccounts.Driver, SeedAccounts.Parent, SeedAccounts.SecondDriver })
        {
            var result = await service.PostJson("/internal/v1/notifications/recipients/drivers", new { organizationId = SeedAccounts.Organization, driverUserId = userId })
                .ExpectAsync(HttpStatusCode.OK);
            result.GetProperty("recipients").GetArrayLength().ShouldBe(0);
        }
    }

    private const string ServiceToken = "directory-fixture-key-0123456789abcdef";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static MonolithApp App() => new(settings: new Dictionary<string, string?> { ["InternalServices:NotificationToken"] = ServiceToken });

    [Fact]
    public async Task DirectoryRequiresServiceCredential()
    {
        await using var app = App();
        using var client = app.CreateClient();
        using var response = await client.PostAsJsonAsync("/internal/v1/notifications/recipients/drivers", new { organizationId = SeedAccounts.Organization }, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task DirectoryUsesVerifiedIamUserAndFiltersRouteAndOrganization()
    {
        await using var app = App();
        using var admin = await app.AdminAsync();
        using var service = app.CreateClient();
        service.DefaultRequestHeaders.Add("X-Service-Token", ServiceToken);
        var profile = await admin.RegisterVerifiedDriverAsync(SeedAccounts.Driver);
        var vehicle = await admin.RegisterVehicleAsync();
        var route = await admin.ReadyRouteAsync(profile, vehicle, students: 0);
        var allowed = await service.PostJson("/internal/v1/notifications/recipients/drivers", new { organizationId = SeedAccounts.Organization, routeId = route })
            .ExpectAsync(HttpStatusCode.OK);
        var recipient = allowed.GetProperty("recipients").EnumerateArray().ShouldHaveSingleItem();
        recipient.GetProperty("userId").GetGuid().ShouldBe(SeedAccounts.Driver);
        recipient.GetProperty("userId").GetGuid().ShouldNotBe(profile);
        var denied = await service.PostJson("/internal/v1/notifications/recipients/drivers", new { organizationId = SeedAccounts.Organization, driverUserId = Guid.NewGuid(), routeId = route })
            .ExpectAsync(HttpStatusCode.OK);
        denied.GetProperty("recipients").GetArrayLength().ShouldBe(0);
        await service.PostJson("/internal/v1/notifications/recipients/drivers", new { organizationId = SeedAccounts.Organization, routeId = Guid.NewGuid() })
            .ExpectAsync(HttpStatusCode.NotFound);
        var foreign = await service.PostJson("/internal/v1/notifications/recipients/drivers", new { organizationId = Guid.NewGuid(), driverUserId = SeedAccounts.Driver })
            .ExpectAsync(HttpStatusCode.OK);
        foreign.GetProperty("recipients").GetArrayLength().ShouldBe(0);
    }
}
