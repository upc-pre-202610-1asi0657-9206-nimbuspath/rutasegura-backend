using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using RutaSegura.Notification.Infrastructure.Messaging.RabbitMq;
using RutaSegura.Notification.Infrastructure.Persistence.EntityFrameworkCore.Configuration;

namespace RutaSegura.Notification.Api.Health;

/// <summary>Disponibilidad local: funciones habilitadas, esquema propio accesible y consumidor suscrito si se habilito.</summary>
public sealed class NotificationReadinessCheck(IServiceScopeFactory scopes, IConfiguration configuration) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var messaging = configuration.GetValue<bool>("NotificationRabbitMq:Enabled");
        if (!messaging && !configuration.GetValue<bool>("NotificationApi:Enabled") && !configuration.GetValue<bool>("NotificationChannels:Enabled"))
            return HealthCheckResult.Unhealthy("Notification no esta habilitado.");
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            if (messaging && scope.ServiceProvider.GetRequiredService<NotificationRabbitMqConsumer>().IsConsuming != true)
                return HealthCheckResult.Unhealthy("Consumidor no disponible.");
            var db = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();
            if ((await db.Database.GetPendingMigrationsAsync(cancellationToken)).Any())
                return HealthCheckResult.Unhealthy("Esquema pendiente de provisionamiento.");
            // Verifica todas las tablas, incluso si el historial de migraciones fue modificado externamente.
            await db.VerifyTablesAsync(cancellationToken);
            if (configuration.GetValue<bool>("NotificationApi:Enabled") && configuration.GetValue("Iam:AccountValidation:Enabled", true))
            {
                var client = scope.ServiceProvider.GetRequiredService<IHttpClientFactory>().CreateClient(Security.NotificationIamAccountVerifier.ClientName);
                using var response = await client.GetAsync("/health", cancellationToken);
                if (!response.IsSuccessStatusCode) return HealthCheckResult.Unhealthy("IAM no disponible.");
            }
            return HealthCheckResult.Healthy();
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Ni excepciones ni cadenas de conexion se exponen por salud o logs de HealthChecks.
            return HealthCheckResult.Unhealthy("Persistencia o dependencia no disponible.");
        }
    }
}
