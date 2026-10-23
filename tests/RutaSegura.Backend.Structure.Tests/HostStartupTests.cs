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
    private static async Task AssertHealthy<TProgram>(bool readinessMapped = false) where TProgram : class
    {
        using var factory = new WebApplicationFactory<TProgram>().WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var response = await client.GetAsync("/health/live", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        // Solo los servicios con adaptadores reales publican readiness (Trip & Tracking: MySQL, RabbitMQ, Redis).
        // Sin dependencias levantadas responde 503, pero el endpoint debe existir.
        using var readiness = await client.GetAsync("/health/ready", TestContext.Current.CancellationToken);
        if (readinessMapped)
            Assert.NotEqual(HttpStatusCode.NotFound, readiness.StatusCode);
        else
            Assert.Equal(HttpStatusCode.NotFound, readiness.StatusCode);
        using var template = await client.GetAsync("/weatherforecast", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, template.StatusCode);
    }

    //[Fact] public Task IdentityHostStarts() => AssertHealthy<iam::Program>();
    [Fact] public Task AdministrationHostStarts() => AssertHealthy<administration::Program>();
    [Fact] public Task TripTrackingHostStarts() => AssertHealthy<trip::Program>(readinessMapped: true);
    [Fact] public Task NotificationHostStarts() => AssertHealthy<notification::Program>();
    [Fact] public Task GatewayHostStarts() => AssertHealthy<gateway::Program>();
}
