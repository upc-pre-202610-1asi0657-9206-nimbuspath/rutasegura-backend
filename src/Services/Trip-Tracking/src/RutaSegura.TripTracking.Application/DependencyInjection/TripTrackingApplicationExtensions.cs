using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RutaSegura.BuildingBlocks.Application.DependencyInjection;

namespace RutaSegura.TripTracking.Application.DependencyInjection;

public static class TripTrackingApplicationExtensions
{
    /// <summary>Registra los casos de uso del servicio. Los adaptadores se registran en AddTripTrackingInfrastructure (Etapa 3).</summary>
    public static IServiceCollection AddTripTrackingApplication(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddRutaSeguraApplication(typeof(TripTrackingApplicationExtensions).Assembly);
        return services;
    }
}
