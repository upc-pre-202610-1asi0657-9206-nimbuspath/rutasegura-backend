extern alias iam;
extern alias administration;
extern alias trip;
extern alias notification;
extern alias gateway;

using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace RutaSegura.Backend.Structure.Tests;

public sealed class HostStartupTests
{
    private static async Task AssertHealthy<TProgram>() where TProgram : class
    {
        using var factory = new WebApplicationFactory<TProgram>().WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var response = await client.GetAsync("/health/live", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        // Infrastructure adapters do not yet exist; do not advertise readiness.
        using var readiness = await client.GetAsync("/health/ready", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, readiness.StatusCode);
        using var template = await client.GetAsync("/weatherforecast", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, template.StatusCode);
    }

    [Fact] public Task IdentityHostStarts() => AssertHealthy<iam::Program>();
    [Fact] public Task AdministrationHostStarts() => AssertHealthy<administration::Program>();
    [Fact] public Task TripTrackingHostStarts() => AssertHealthy<trip::Program>();
    [Fact] public Task NotificationHostStarts() => AssertHealthy<notification::Program>();
    [Fact] public Task GatewayHostStarts() => AssertHealthy<gateway::Program>();
}
