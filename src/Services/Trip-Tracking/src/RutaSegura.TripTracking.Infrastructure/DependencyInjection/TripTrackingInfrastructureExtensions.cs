using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace RutaSegura.TripTracking.Infrastructure.DependencyInjection;

public static class TripTrackingInfrastructureExtensions
{
    public static IServiceCollection AddTripTrackingInfrastructure(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        // Los puertos existentes aún no tienen adaptadores productivos.
        return services;
    }
}
