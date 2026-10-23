using MySql.Data.MySqlClient;

namespace RutaSegura.Notification.Infrastructure.Persistence.EntityFrameworkCore.Configuration;

internal static class NotificationDatabaseConnection
{
    internal static void Validate(string connectionString)
    {
        var connection = new MySqlConnectionStringBuilder(connectionString);
        if (string.IsNullOrWhiteSpace(connection.Database) ||
            new[] { "mysql", "sys", "information_schema", "performance_schema" }.Contains(connection.Database, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException("Configura una base exclusiva de Notification, nunca una base del sistema.", nameof(connectionString));
    }
}
