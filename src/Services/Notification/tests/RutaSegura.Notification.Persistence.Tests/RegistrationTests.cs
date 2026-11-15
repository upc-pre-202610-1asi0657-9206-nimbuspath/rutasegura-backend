using Microsoft.Extensions.DependencyInjection;
using RutaSegura.BuildingBlocks.Application.Persistence;
using RutaSegura.Notification.Application.Internal.OutboundServices;
using RutaSegura.Notification.Domain.Repositories;
using RutaSegura.Notification.Infrastructure.DependencyInjection;
using RutaSegura.Notification.Infrastructure.Persistence.EntityFrameworkCore.Configuration;
using RutaSegura.Notification.Infrastructure.Persistence.EntityFrameworkCore.Repositories;

namespace RutaSegura.Notification.Persistence.Tests;

public sealed class RegistrationTests
{
    [Fact]
    public void ConfiguredPersistenceUsesRealScopedRepositories_AndDoesNotConnectOnRegistration()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddNotificationPersistence("Server=127.0.0.1;Port=1;Database=notification_registration_only;UserID=unused;Password=unused",
            TestSecrets.EncryptionKey);
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        using var scope = provider.CreateScope();
        var dependencies = scope.ServiceProvider;
        dependencies.GetRequiredService<INotificationRepository>().ShouldBeOfType<NotificationRepository>();
        dependencies.GetRequiredService<IAnnouncementRepository>().ShouldBeOfType<AnnouncementRepository>();
        dependencies.GetRequiredService<IPushSubscriptionRepository>().ShouldBeOfType<PushSubscriptionRepository>();
        dependencies.GetRequiredService<IProcessedNotificationEventRepository>().ShouldBeOfType<ProcessedNotificationEventRepository>();
        dependencies.GetRequiredService<IUnitOfWork>().ShouldBeOfType<NotificationUnitOfWork>();
        dependencies.GetRequiredService<INotificationDispatchQueue>().ShouldBeSameAs(dependencies.GetRequiredService<INotificationDispatchRepository>());
        dependencies.GetRequiredService<NotificationDbContext>().ShouldBeSameAs(dependencies.GetRequiredService<NotificationDbContext>());
        dependencies.GetService<IPushNotificationSender>().ShouldBeNull();
        dependencies.GetService<IRealtimeNotificationSender>().ShouldBeNull();
    }

    [Theory]
    [InlineData("mysql")]
    [InlineData("sys")]
    [InlineData("information_schema")]
    [InlineData("performance_schema")]
    public void SystemDatabasesCannotBeConfiguredAsNotificationDatabase(string database) =>
        Should.Throw<ArgumentException>(() => new ServiceCollection().AddNotificationPersistence(
            $"Server=127.0.0.1;Database={database};UserID=unused", TestSecrets.EncryptionKey));

    [Fact]
    public void ConfiguredDatabaseRequiresAnExternalValidEncryptionKey() =>
        Should.Throw<ArgumentException>(() => new ServiceCollection().AddNotificationPersistence(
            "Server=127.0.0.1;Database=notification_registration_only;UserID=unused", ""));
}
