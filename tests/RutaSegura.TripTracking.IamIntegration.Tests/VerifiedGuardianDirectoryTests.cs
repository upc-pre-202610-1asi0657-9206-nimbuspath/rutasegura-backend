using System.Net;
using System.Net.Http.Json;
using RutaSegura.TripTracking.Application.Abstractions;
using RutaSegura.TripTracking.Domain.Common;
using RutaSegura.TripTracking.Infrastructure.Integration;
namespace RutaSegura.TripTracking.IamIntegration.Tests;
public sealed class VerifiedGuardianDirectoryTests
{
    private sealed class Handler(Guid guardian, Guid organization, Guid student) : HttpMessageHandler
    {
        public bool Revoked; public bool WrongTenant; public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new
            { userId = guardian, organizationId = WrongTenant ? Guid.NewGuid() : organization, studentIds = Revoked ? Array.Empty<Guid>() : new[] { student } }) });
        }
    }
    [Fact]
    public async Task NoPositiveCacheAndForeignScopeResponsesFailClosed()
    {
        var guardian = Guid.NewGuid(); var organization = Guid.NewGuid(); var student = Guid.NewGuid();
        using var handler = new Handler(guardian, organization, student);
        using var http = new HttpClient(handler) { BaseAddress = new("https://directory.test/") };
        var directory = new VerifiedGuardianDirectory(http);
        (await directory.GetStudentsForOrganizationAsync(new(guardian), organization, TestContext.Current.CancellationToken)).ShouldHaveSingleItem();
        handler.Revoked = true;
        (await directory.GetStudentsForOrganizationAsync(new(guardian), organization, TestContext.Current.CancellationToken)).ShouldBeEmpty();
        handler.Calls.ShouldBe(2);
        handler.WrongTenant = true;
        await Should.ThrowAsync<GuardianDirectoryUnavailableException>(() => directory.GetStudentsForOrganizationAsync(new(guardian), organization, TestContext.Current.CancellationToken));
    }
}
