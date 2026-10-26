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
        // Solo publican readiness los hosts con dependencias reales (Trip & Tracking: MySQL, RabbitMQ, Redis; Gateway: servicios).
        // Sin dependencias levantadas responde 503, pero el endpoint debe existir.
        using var readiness = await client.GetAsync("/health/ready", TestContext.Current.CancellationToken);
        if (readinessMapped)
            Assert.NotEqual(HttpStatusCode.NotFound, readiness.StatusCode);
        else
            Assert.Equal(HttpStatusCode.NotFound, readiness.StatusCode);
        using var template = await client.GetAsync("/weatherforecast", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, template.StatusCode);
    }

    [Fact] public Task TripTrackingHostStarts() => AssertHealthy<trip::Program>(readinessMapped: true);
    [Fact] public Task NotificationHostStarts() => AssertHealthy<notification::Program>();
    // El Gateway publica readiness: responde 200 solo si el monolito y Trip & Tracking responden.
    [Fact] public Task GatewayHostStarts() => AssertHealthy<gateway::Program>(readinessMapped: true);
}
