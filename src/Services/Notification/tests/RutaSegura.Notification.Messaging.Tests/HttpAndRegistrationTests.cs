using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RutaSegura.Notification.Application.DependencyInjection;
using RutaSegura.Notification.Application.Internal.OutboundServices;
using RutaSegura.Notification.Application.Model.Results;
using RutaSegura.Notification.Infrastructure.DependencyInjection;
using RutaSegura.Notification.Infrastructure.Integration;
using RutaSegura.Notification.Infrastructure.Messaging.RabbitMq;

namespace RutaSegura.Notification.Messaging.Tests;

public sealed class HttpAndRegistrationTests
{
    [Fact]
    public async Task RecipientAdapterUsesInternalContractAndRejectsCrossTenantResponses()
    {
        var stub = new HttpStub(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new AdministrationRecipientDirectory.RecipientResponse(MessageSamples.Organization,
                [new(Guid.NewGuid(), MessageSamples.Guardian, MessageSamples.Student)]))
        });
        var adapter = new AdministrationRecipientDirectory(Client(stub));
        await Should.ThrowAsync<HttpRequestException>(() => adapter.ForStudentsAsync(MessageSamples.Organization, [MessageSamples.Student], TestContext.Current.CancellationToken));
        stub.Path.ShouldBe("/internal/v1/notifications/recipients/students");
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task UpstreamFailureNeverBecomesAnEmptySuccessfulAudience(HttpStatusCode status)
    {
        var adapter = new AdministrationRecipientDirectory(Client(new(_ => new(status))));
        await Should.ThrowAsync<HttpRequestException>(() => adapter.ForStudentsAsync(MessageSamples.Organization, [MessageSamples.Student], TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AuthoritativeEmptyListIsAccepted()
    {
        var adapter = new AdministrationRecipientDirectory(Client(new(_ => new(HttpStatusCode.OK)
        { Content = JsonContent.Create(new AdministrationRecipientDirectory.RecipientResponse(MessageSamples.Organization, [])) })));
        (await adapter.ForStudentsAsync(MessageSamples.Organization, [MessageSamples.Student], TestContext.Current.CancellationToken)).ShouldBeEmpty();
    }

    [Fact]
    public async Task TripContextCannotTrustAnAnswerAboutAnotherOrganization()
    {
        var adapter = new TripNotificationContextClient(Client(new(_ => new(HttpStatusCode.OK)
        { Content = JsonContent.Create(new TripNotificationContextClient.ContextResponse(Guid.NewGuid(), MessageSamples.Trip, true)) })));
        await Should.ThrowAsync<HttpRequestException>(() => adapter.IsActiveAsync(MessageSamples.Organization, MessageSamples.Trip, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("https://trip.internal/", false, true)]
    [InlineData("http://trip.internal/", false, false)]
    [InlineData("http://localhost:5001/", true, true)]
    [InlineData("https://user:password@trip.internal/", false, false)]
    public void ServiceEndpointsRequireTlsAndNoEmbeddedCredentials(string url, bool insecure, bool valid)
    {
        var endpoint = new NotificationDependencyOptions.ServiceEndpoint { BaseUrl = url, ServiceToken = new string('s', 32), AllowInsecureHttp = insecure };
        if (valid) endpoint.Validate().ShouldNotBeNull();
        else Should.Throw<InvalidOperationException>(() => endpoint.Validate());
    }

    [Fact]
    public void EnablingWorkerRequiresDatabaseAndUpstreamConfiguration()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["NotificationRabbitMq:Enabled"] = "true" }).Build();
        Should.Throw<InvalidOperationException>(() => new ServiceCollection().AddNotificationInfrastructure(config));
    }

    [Fact]
    public async Task ProductionRegistrationHasRealHttpPortsAndNoAuthenticatedBackgroundUser()
    {
        var values = new Dictionary<string, string?>
        {
            ["NotificationRabbitMq:Enabled"] = "true", ["NotificationRabbitMq:UserName"] = "test", ["NotificationRabbitMq:Password"] = "test",
            ["ConnectionStrings:NotificationDatabase"] = "Server=127.0.0.1;Port=1;Database=notification_registration_test;UserID=unused",
            ["NotificationPersistence:PushTokenEncryptionKey"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        };
        foreach (var dependency in new[] { "Administration", "TripTracking" })
        {
            values[$"NotificationDependencies:{dependency}:BaseUrl"] = "https://service.internal/";
            values[$"NotificationDependencies:{dependency}:ServiceToken"] = new string('s', 32);
        }
        var config = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var services = new ServiceCollection().AddLogging().AddNotificationApplication().AddNotificationInfrastructure(config);
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<INotificationRecipientDirectory>().ShouldBeOfType<AdministrationRecipientDirectory>();
        scope.ServiceProvider.GetRequiredService<ITripNotificationContext>().ShouldBeOfType<TripNotificationContextClient>();
        scope.ServiceProvider.GetRequiredService<INotificationUserContext>().Current.ShouldBeNull();
        provider.GetRequiredService<NotificationRabbitMqConsumer>().IsConsuming.ShouldBeFalse();
    }

    private static HttpClient Client(HttpStub stub) => new(stub) { BaseAddress = new Uri("https://service.internal/") };
    private sealed class HttpStub(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        public string? Path { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Path = request.RequestUri!.AbsolutePath;
            return Task.FromResult(response(request));
        }
    }
}
