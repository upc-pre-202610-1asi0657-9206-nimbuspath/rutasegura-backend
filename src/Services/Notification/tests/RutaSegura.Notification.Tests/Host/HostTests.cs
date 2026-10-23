extern alias notificationApi;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using RutaSegura.Notification.Application.Internal.OutboundServices;
using RutaSegura.Notification.Domain.Repositories;
using NotificationApiProgram = notificationApi::Program;

namespace RutaSegura.Notification.Tests.Host;

public sealed class HostTests
{
    [Fact]
    public async Task PhaseOneHostStarts_WithoutRegisteringFakeProductionAdapters()
    {
        await using var factory = new NotificationHostFactory();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/health/live", TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        factory.Services.GetService<INotificationRepository>().ShouldBeNull();
        factory.Services.GetService<INotificationDispatchQueue>().ShouldBeNull();
        factory.Services.GetService<IPushNotificationSender>().ShouldBeNull();
    }

    private sealed class NotificationHostFactory : WebApplicationFactory<NotificationApiProgram>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "RutaSegura.slnx")))
                directory = directory.Parent;
            if (directory is null) throw new DirectoryNotFoundException("No se encontro la raiz del repositorio.");
            builder.UseContentRoot(Path.Combine(directory.FullName, "src", "Services", "Notification", "src", "RutaSegura.Notification.Api"));
            builder.UseEnvironment("Development");
        }
    }
}
