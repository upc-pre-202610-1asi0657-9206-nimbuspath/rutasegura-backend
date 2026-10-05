using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace RutaSegura.IAM.Infrastructure.DependencyInjection;

public static class IAMInfrastructureExtensions
{
    public static IServiceCollection AddIAMInfrastructure(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        // No se registran repositorios ni unidades de trabajo ficticias en producción.
        // Persistencia y RabbitMQ se incorporan con sus tareas del Sprint 1.
        return services;
    }
}
