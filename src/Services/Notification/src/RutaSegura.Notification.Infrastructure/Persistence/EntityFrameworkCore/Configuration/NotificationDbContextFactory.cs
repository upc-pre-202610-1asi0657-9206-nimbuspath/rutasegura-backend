using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using RutaSegura.Notification.Infrastructure.Security;

namespace RutaSegura.Notification.Infrastructure.Persistence.EntityFrameworkCore.Configuration;

public sealed class NotificationDbContextFactory : IDesignTimeDbContextFactory<NotificationDbContext>
{
    public NotificationDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("ConnectionStrings__NotificationDatabase")
            ?? throw new InvalidOperationException("Configura ConnectionStrings__NotificationDatabase para las migraciones.");
        var key = Environment.GetEnvironmentVariable("NotificationPersistence__PushTokenEncryptionKey")
            ?? throw new InvalidOperationException("Configura NotificationPersistence__PushTokenEncryptionKey fuera del repositorio.");
        NotificationDatabaseConnection.Validate(connection);
        var options = new DbContextOptionsBuilder<NotificationDbContext>().UseMySQL(connection).Options;
        return new NotificationDbContext(options, new AesGcmPushTokenProtector(key));
    }
}
