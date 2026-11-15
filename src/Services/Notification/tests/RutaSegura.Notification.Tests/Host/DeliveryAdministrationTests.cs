using System.Net;
using System.Net.Http.Json;
using RutaSegura.Notification.Domain.Model.Entities;
using RutaSegura.Notification.Domain.Model.ValueObjects;

namespace RutaSegura.Notification.Tests.Host;

public sealed class DeliveryAdministrationTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    [Fact]
    public async Task ReplayIsAdminOnlyScopedIdempotentAndCannotChangeItsReasonOrRepeatAnAcceptedDelivery()
    {
        await using var factory=new NotificationApiFactory();
        var notice=factory.SeedNotice();
        var delivery=NotificationDelivery.Schedule(notice,NotificationChannel.Realtime,factory.Guardian,factory.Now);
        delivery.Fail("CHANNEL_DEPENDENCY_UNAVAILABLE",factory.Now); factory.Store.Add(delivery);
        using var client=factory.CreateClient();
        var requestId=Guid.NewGuid();
        var url=$"/api/v1/notifications/deliveries/{delivery.Id}/replay";
        client.DefaultRequestHeaders.Authorization=new("Bearer",factory.Token());
        (await client.PostAsJsonAsync(url,new{requestId,reasonCode="ProviderRecovered"},Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        client.DefaultRequestHeaders.Authorization=new("Bearer",factory.Token(role:"ADMIN"));
        (await client.PostAsJsonAsync(url,new{requestId,reasonCode="ProviderRecovered"},Ct)).StatusCode.ShouldBe(HttpStatusCode.Accepted);
        (await client.PostAsJsonAsync(url,new{requestId,reasonCode="ProviderRecovered"},Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await client.PostAsJsonAsync(url,new{requestId,reasonCode="ManualReview"},Ct)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        factory.Store.Replays.ShouldHaveSingleItem().RequestedByUserId.ShouldBe(factory.Guardian);
        delivery.AttemptCount.ShouldBe(1); delivery.CycleAttemptCount.ShouldBe(0); delivery.ReplayCount.ShouldBe(1);
        delivery.AcceptByProvider("test-provider",DateTimeOffset.UtcNow);
        (await client.PostAsJsonAsync(url,new{requestId=Guid.NewGuid(),reasonCode="ProviderRecovered"},Ct)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task ForeignTenantAndRevokedOperatorCannotInspectOrReplayAnotherOrganizationsFailures()
    {
        await using var factory=new NotificationApiFactory();
        var notice=factory.SeedNotice();
        var delivery=NotificationDelivery.Schedule(notice,NotificationChannel.Realtime,factory.Guardian,factory.Now);
        delivery.Fail("CHANNEL_DEPENDENCY_UNAVAILABLE",factory.Now); factory.Store.Add(delivery);
        var other=Guid.NewGuid(); factory.Directory.Operators.Add(new(other,factory.Guardian));
        using var client=factory.CreateClient();
        client.DefaultRequestHeaders.Authorization=new("Bearer",factory.Token(organization:other,role:"ADMIN"));
        (await client.PostAsJsonAsync($"/api/v1/notifications/deliveries/{delivery.Id}/replay",new{requestId=Guid.NewGuid(),reasonCode="ManualReview"},Ct))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await client.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/v1/notifications/deliveries/failed",Ct)).GetArrayLength().ShouldBe(0);
        factory.Directory.Operators.Clear();
        client.DefaultRequestHeaders.Authorization=new("Bearer",factory.Token(role:"ADMIN"));
        (await client.GetAsync("/api/v1/notifications/deliveries/failed",Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ReplayRevalidatesTheRecipientAndHasAFiniteLifetimeBudget()
    {
        await using var factory=new NotificationApiFactory();
        var notice=factory.SeedNotice();
        var delivery=NotificationDelivery.Schedule(notice,NotificationChannel.Realtime,factory.Guardian,factory.Now);
        delivery.Fail("CHANNEL_DEPENDENCY_UNAVAILABLE",factory.Now); factory.Store.Add(delivery);
        using var client=factory.CreateClient(); client.DefaultRequestHeaders.Authorization=new("Bearer",factory.Token(role:"ADMIN"));
        var url=$"/api/v1/notifications/deliveries/{delivery.Id}/replay";
        factory.Directory.Students.Clear();
        (await client.PostAsJsonAsync(url,new{requestId=Guid.NewGuid(),reasonCode="ProviderRecovered"},Ct)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        factory.Directory.Students.Add(new(factory.Organization,factory.Guardian,factory.Student));
        for(var cycle=0;cycle<3;cycle++)
        {
            (await client.PostAsJsonAsync(url,new{requestId=Guid.NewGuid(),reasonCode="ProviderRecovered"},Ct)).StatusCode.ShouldBe(HttpStatusCode.Accepted);
            delivery.Fail("CHANNEL_DEPENDENCY_UNAVAILABLE",DateTimeOffset.UtcNow);
        }
        (await client.PostAsJsonAsync(url,new{requestId=Guid.NewGuid(),reasonCode="ProviderRecovered"},Ct)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        delivery.AttemptCount.ShouldBe(4); delivery.CycleAttemptCount.ShouldBe(1); delivery.ReplayCount.ShouldBe(3);
    }
}
