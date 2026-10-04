using RutaSegura.BuildingBlocks.Application.Results;

namespace RutaSegura.BuildingBlocks.Application.Messaging;

/// <summary>Intención de cambiar estado. Pasa por Logging → Validation → Transaction.</summary>
public interface ICommand<TResponse>;

/// <summary>Lectura sin efectos. No abre transacción.</summary>
public interface IQuery<TResponse>;

public interface ICommandHandler<in TCommand, TResponse>
    where TCommand : ICommand<TResponse>
{
    Task<Result<TResponse>> HandleAsync(TCommand command, CancellationToken cancellationToken);
}

public interface IQueryHandler<in TQuery, TResponse>
    where TQuery : IQuery<TResponse>
{
    Task<Result<TResponse>> HandleAsync(TQuery query, CancellationToken cancellationToken);
}

/// <summary>Mediator propio: los controllers solo conocen IDispatcher (R-02 de 5.1.4).</summary>
public interface IDispatcher
{
    Task<Result<TResponse>> SendAsync<TResponse>(ICommand<TResponse> command, CancellationToken cancellationToken = default);

    Task<Result<TResponse>> QueryAsync<TResponse>(IQuery<TResponse> query, CancellationToken cancellationToken = default);
}
