using Microsoft.Extensions.DependencyInjection;
using RutaSegura.BuildingBlocks.Application.DependencyInjection;

namespace RutaSegura.IAM.Application.DependencyInjection;

public static class IAMApplicationExtensions
{
    public static IServiceCollection AddIAMApplication(this IServiceCollection services) =>
        services.AddRutaSeguraApplication(typeof(IAMApplicationExtensions).Assembly);
}
