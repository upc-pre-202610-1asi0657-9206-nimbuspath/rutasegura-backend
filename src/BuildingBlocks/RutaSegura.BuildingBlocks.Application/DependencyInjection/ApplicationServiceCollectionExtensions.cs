using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using RutaSegura.BuildingBlocks.Application.Behaviors;
using RutaSegura.BuildingBlocks.Application.Messaging;
using RutaSegura.BuildingBlocks.Application.Validation;

namespace RutaSegura.BuildingBlocks.Application.DependencyInjection;

public static class ApplicationServiceCollectionExtensions
{
    // Del más interno al más externo.
    private static readonly Type[] CommandDecorators =
    [
        typeof(TransactionCommandDecorator<,>),
        typeof(ValidationCommandDecorator<,>),
        typeof(LoggingCommandDecorator<,>)
    ];

    /// <summary>
    /// Escanea el ensamblado de Application y registra: IDispatcher, handlers de comandos
    /// (envueltos en Logging → Validation → Transaction), handlers de queries y validadores.
    /// </summary>
    public static IServiceCollection AddRutaSeguraApplication(this IServiceCollection services, Assembly applicationAssembly)
    {
        services.AddScoped<IDispatcher, Dispatcher>();

        foreach (var type in applicationAssembly.GetTypes().Where(t => t is { IsClass: true, IsAbstract: false, IsGenericTypeDefinition: false }))
        {
            foreach (var contract in type.GetInterfaces().Where(i => i.IsGenericType))
            {
                var definition = contract.GetGenericTypeDefinition();

                if (definition == typeof(ICommandHandler<,>))
                    RegisterDecoratedCommandHandler(services, type, contract);
                else if (definition == typeof(IQueryHandler<,>))
                    services.AddScoped(contract, type);
                else if (definition == typeof(IValidator<>))
                    services.AddScoped(contract, type);
            }
        }

        return services;
    }

    private static void RegisterDecoratedCommandHandler(IServiceCollection services, Type handlerType, Type contract)
    {
        services.AddScoped(handlerType);
        var genericArguments = contract.GetGenericArguments();

        services.AddScoped(contract, provider =>
        {
            var current = provider.GetRequiredService(handlerType);
            foreach (var decorator in CommandDecorators)
            {
                var closed = decorator.MakeGenericType(genericArguments);
                current = ActivatorUtilities.CreateInstance(provider, closed, current);
            }
            return current;
        });
    }
}
