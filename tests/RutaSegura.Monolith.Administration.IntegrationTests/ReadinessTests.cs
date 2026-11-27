using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Monolith.Shared.Infrastructure.Health;
using RutaSegura.Monolith.Administration.IntegrationTests.Support;
namespace RutaSegura.Monolith.Administration.IntegrationTests;
public sealed class ReadinessTests
{
    [Fact]
    public async Task ReadinessFailsWhenDatabaseInitializationIsIncompleteWhileLivenessRemainsHealthy()
    {
        await using var app = new MonolithApp();
        using var client = app.CreateClient();
        (await client.GetAsync("/health/ready", TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.OK);
        app.Services.GetRequiredService<MonolithStartupState>().MigrationsCompleted = false;
        (await client.GetAsync("/health/ready", TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        (await client.GetAsync("/health/live", TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
