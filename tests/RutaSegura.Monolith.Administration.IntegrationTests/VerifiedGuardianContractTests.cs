using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Monolith.Stakeholder.Infrastructure.Messaging;
using RutaSegura.Monolith.Administration.IntegrationTests.Support;
namespace RutaSegura.Monolith.Administration.IntegrationTests;
public sealed class VerifiedGuardianContractTests
{
    private const string ServiceToken = "directory-fixture-key-0123456789abcdef";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static MonolithApp App() => new(settings: new Dictionary<string, string?> { ["InternalServices:NotificationToken"] = ServiceToken });
    private static HttpClient Directory(MonolithApp app)
    {
        var client = app.CreateClient(); client.DefaultRequestHeaders.Add("X-Service-Token", ServiceToken); return client;
    }
    private static async Task<JsonElement> Links(HttpClient service, Guid organization, Guid guardian) =>
        await service.PostJson("/internal/v1/notifications/student-links", new { organizationId = organization, userId = guardian }).ExpectAsync(HttpStatusCode.OK);
    [Fact]
    public async Task EndingEnrollmentStopsNewTripRecipientsButDoesNotRevokeTheVerifiedGuardianLink()
    {
        await using var app = App(); using var admin = await app.AdminAsync(); using var service = Directory(app);
        var student = await admin.RegisterStudentAsync();
        var driver = await admin.RegisterVerifiedDriverAsync(SeedAccounts.Driver);
        var vehicle = await admin.RegisterVehicleAsync();
        var route = await admin.ReadyRouteAsync(driver, vehicle, students: 0);
        var active = await admin.PostJson($"/api/v1/routes/{route}/activate").ExpectAsync(HttpStatusCode.OK);
        using var parent = await app.SignInAsync(SeedAccounts.ParentEmail, SeedAccounts.ParentPassword);
        var enrollment = await parent.PostJson($"/api/v1/stakeholders/me/students/{student}/enrollments", new { enrollmentCode = active.GetProperty("enrollmentCode").GetString() }).ExpectAsync(HttpStatusCode.Created);
        var pending = await parent.Get("/api/v1/stakeholders/me/guardian-links").ExpectAsync(HttpStatusCode.OK);
        pending[0].GetProperty("status").GetString().ShouldBe("PENDING");
        await admin.PostJson($"/api/v1/stakeholders/administration/guardian-links/{SeedAccounts.Parent}/{student}/verify", new { reference = "Verified by operation" }).ExpectAsync(HttpStatusCode.OK);
        var recipients = await service.PostJson("/internal/v1/notifications/recipients/students", new { organizationId = SeedAccounts.Organization, studentIds = new[] { student } }).ExpectAsync(HttpStatusCode.OK);
        recipients.GetProperty("recipients").GetArrayLength().ShouldBe(1);
        await admin.PostJson($"/api/v1/stakeholders/enrollments/{enrollment.Id()}/cancel", new { reason = "End transport service" }).ExpectAsync(HttpStatusCode.OK);
        (await Links(service, SeedAccounts.Organization, SeedAccounts.Parent)).GetProperty("studentIds").GetArrayLength().ShouldBe(1);
        recipients = await service.PostJson("/internal/v1/notifications/recipients/students", new { organizationId = SeedAccounts.Organization, studentIds = new[] { student } }).ExpectAsync(HttpStatusCode.OK);
        recipients.GetProperty("recipients").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task VerificationIsExplicitScopedAndRevocationChangesDirectoryImmediately()
    {
        await using var app = App(); using var admin = await app.AdminAsync(); using var service = Directory(app);
        var student = await admin.RegisterStudentAsync();
        (await Links(service, SeedAccounts.Organization, SeedAccounts.Parent)).GetProperty("studentIds").GetArrayLength().ShouldBe(0);
        using var parent = await app.SignInAsync(SeedAccounts.ParentEmail, SeedAccounts.ParentPassword);
        (await parent.PostAsJsonAsync($"/api/v1/stakeholders/administration/guardian-links/{SeedAccounts.Parent}/{student}/verify", new { reference = "self-approval" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var path = $"/api/v1/stakeholders/administration/guardian-links/{SeedAccounts.Parent}/{student}";
        await admin.PostJson(path + "/verify", new { reference = "Verificación administrativa presencial" }).ExpectAsync(HttpStatusCode.OK);
        (await Links(service, SeedAccounts.Organization, SeedAccounts.Parent)).GetProperty("studentIds")[0].GetGuid().ShouldBe(student);
        (await Links(service, Guid.NewGuid(), SeedAccounts.Parent)).GetProperty("studentIds").GetArrayLength().ShouldBe(0);
        var foreign = await app.ProvisionOrganizationAsync("Otra", "other-admin@example.test");
        (await foreign.Administrator.PostAsJsonAsync(path + "/revoke", new { reference = "foreign" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        await admin.PostJson(path + "/revoke", new { reference = "Revocación solicitada" }).ExpectAsync(HttpStatusCode.OK);
        (await Links(service, SeedAccounts.Organization, SeedAccounts.Parent)).GetProperty("studentIds").GetArrayLength().ShouldBe(0);
        var events = await app.WithDbAsync(db => db.Set<StakeholderOutboxMessage>().Select(m => m.MessageType).ToListAsync(Ct));
        events.ShouldBe(["stakeholder.guardian-student-linked", "stakeholder.guardian-student-unlinked"], ignoreOrder: true);
    }
    [Fact]
    public async Task PublicJwtCannotUseInternalDirectoryAndForeignStudentsCannotBeAssigned()
    {
        await using var app = App(); using var admin = await app.AdminAsync();
        var denied = await admin.PostAsJsonAsync("/internal/v1/notifications/student-links", new { organizationId = SeedAccounts.Organization, userId = SeedAccounts.Parent }, Ct);
        denied.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        var student = await admin.RegisterStudentAsync();
        var foreign = await app.ProvisionOrganizationAsync("Otra", "other-roster@example.test");
        var route = await foreign.Administrator.CreateRouteAsync(); var stops = await foreign.Administrator.AddTwoStopsAsync(route);
        (await foreign.Administrator.PutAsync($"/api/v1/routes/{route}/stops/{stops.StopId(0)}/students/{student}", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
    [Fact]
    public async Task MissingServiceSecretFailsClosedAndAnnouncementRejectsForeignRoute()
    {
        await using var unconfigured = new MonolithApp(); using var anonymous = unconfigured.CreateClient();
        (await anonymous.PostAsJsonAsync("/internal/v1/notifications/student-links", new { organizationId = SeedAccounts.Organization, userId = SeedAccounts.Parent }, Ct)).StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        await using var app = App(); using var admin = await app.AdminAsync(); using var service = Directory(app);
        var route = await admin.CreateRouteAsync();
        (await service.PostAsJsonAsync("/internal/v1/notifications/recipients/announcement", new { organizationId = Guid.NewGuid(), routeId = route }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
    [Fact]
    public async Task RoadCalculationRequiresOwnedRouteAndMissingProviderKeyDoesNotPersistFakeGeometry()
    {
        await using var app = App(); using var admin = await app.AdminAsync();
        var route = await admin.CreateRouteAsync(); await admin.AddTwoStopsAsync(route);
        var unavailable = await admin.PostAsync($"/api/v1/routes/{route}/itinerary", null, Ct);
        unavailable.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        (await admin.Get($"/api/v1/routes/{route}").ExpectAsync(HttpStatusCode.OK)).GetProperty("itinerary").ValueKind.ShouldBe(JsonValueKind.Null);
        var foreign = await app.ProvisionOrganizationAsync("Otra", "maps-foreign@example.test");
        (await foreign.Administrator.PostAsync($"/api/v1/routes/{route}/itinerary", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
