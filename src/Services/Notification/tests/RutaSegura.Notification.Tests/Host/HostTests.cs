extern alias notificationApi;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using System.Data;
using Microsoft.EntityFrameworkCore;
using RutaSegura.Notification.Infrastructure.Persistence.EntityFrameworkCore.Configuration;
using RutaSegura.Notification.Infrastructure.Persistence.EntityFrameworkCore.Repositories;
using RutaSegura.Notification.Application.Internal.OutboundServices;
using RutaSegura.Notification.Domain.Repositories;
using NotificationApiProgram = notificationApi::Program;

namespace RutaSegura.Notification.Tests.Host;

public sealed class HostTests
{
    [Fact]
    public async Task HostWithoutDatabaseConfigurationStarts_WithoutRegisteringFakeProductionAdapters()
    {
        await using var factory = new NotificationHostFactory();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/health/live", TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        factory.Services.GetService<INotificationRepository>().ShouldBeNull();
        factory.Services.GetService<INotificationDispatchQueue>().ShouldBeNull();
        factory.Services.GetService<IPushNotificationSender>().ShouldBeNull();
    }

    [Fact]
    public async Task HostWithDatabaseConfigurationRegistersRealRepositories_WithoutMigratingOrConnectingAtStartup()
    {
        await using var factory = new NotificationHostFactory(configureDatabase: true);
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/health/live", TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<INotificationRepository>().ShouldBeOfType<NotificationRepository>();
        scope.ServiceProvider.GetRequiredService<NotificationDbContext>().Database.GetDbConnection().State.ShouldBe(ConnectionState.Closed);
        scope.ServiceProvider.GetService<IPushNotificationSender>().ShouldBeNull();
    }

    private sealed class NotificationHostFactory(bool configureDatabase = false) : WebApplicationFactory<NotificationApiProgram>
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

        protected override IHost CreateHost(IHostBuilder builder)
        {
            // La seleccion de adaptadores ocurre en Program: la configuracion debe existir antes del composition root.
            builder.ConfigureHostConfiguration(configuration => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:NotificationDatabase"] = configureDatabase
                    ? "Server=127.0.0.1;Port=1;Database=notification_host_test;UserID=unused;Password=unused" : "",
                ["NotificationPersistence:PushTokenEncryptionKey"] = configureDatabase
                    ? Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)) : ""
            }));
            return base.CreateHost(builder);
        }
    }
}
