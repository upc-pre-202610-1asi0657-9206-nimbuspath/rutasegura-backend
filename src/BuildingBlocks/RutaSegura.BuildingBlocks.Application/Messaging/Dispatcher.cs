using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using RutaSegura.BuildingBlocks.Application.Results;

namespace RutaSegura.BuildingBlocks.Application.Messaging;

/// <summary>
/// Resuelve el handler (ya envuelto en sus decoradores) del contenedor de DI.
/// Usa wrappers genéricos cacheados por tipo en lugar de <c>dynamic</c>: es más rápido
/// y funciona aunque los handlers sean <c>internal</c>.
/// </summary>
public sealed class Dispatcher(IServiceProvider serviceProvider) : IDispatcher
{
    private static readonly ConcurrentDictionary<Type, object> Wrappers = new();

    public Task<Result<TResponse>> SendAsync<TResponse>(ICommand<TResponse> command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var wrapper = (RequestWrapper<TResponse>)Wrappers.GetOrAdd(command.GetType(),
            t => Activator.CreateInstance(typeof(CommandWrapper<,>).MakeGenericType(t, typeof(TResponse)))!);
        return wrapper.HandleAsync(command, serviceProvider, cancellationToken);
    }

    public Task<Result<TResponse>> QueryAsync<TResponse>(IQuery<TResponse> query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var wrapper = (RequestWrapper<TResponse>)Wrappers.GetOrAdd(query.GetType(),
            t => Activator.CreateInstance(typeof(QueryWrapper<,>).MakeGenericType(t, typeof(TResponse)))!);
        return wrapper.HandleAsync(query, serviceProvider, cancellationToken);
    }

    private abstract class RequestWrapper<TResponse>
    {
        public abstract Task<Result<TResponse>> HandleAsync(object request, IServiceProvider services, CancellationToken cancellationToken);
    }

    private sealed class CommandWrapper<TCommand, TResponse> : RequestWrapper<TResponse>
        where TCommand : ICommand<TResponse>
    {
        public override Task<Result<TResponse>> HandleAsync(object request, IServiceProvider services, CancellationToken cancellationToken) =>
            services.GetRequiredService<ICommandHandler<TCommand, TResponse>>().HandleAsync((TCommand)request, cancellationToken);
    }

    private sealed class QueryWrapper<TQuery, TResponse> : RequestWrapper<TResponse>
        where TQuery : IQuery<TResponse>
    {
        public override Task<Result<TResponse>> HandleAsync(object request, IServiceProvider services, CancellationToken cancellationToken) =>
            services.GetRequiredService<IQueryHandler<TQuery, TResponse>>().HandleAsync((TQuery)request, cancellationToken);
    }
}
