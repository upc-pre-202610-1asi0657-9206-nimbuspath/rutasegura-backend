using Microsoft.Extensions.DependencyInjection;
using RutaSegura.BuildingBlocks.Application.DependencyInjection;

namespace RutaSegura.Administration.Application.DependencyInjection;

public static class AdministrationApplicationExtensions
{
    public static IServiceCollection AddAdministrationApplication(this IServiceCollection services) =>
        services.AddRutaSeguraApplication(typeof(AdministrationApplicationExtensions).Assembly);
}
