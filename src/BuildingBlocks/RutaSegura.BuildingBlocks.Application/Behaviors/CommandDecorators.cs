using System.Diagnostics;
using Microsoft.Extensions.Logging;
using RutaSegura.BuildingBlocks.Application.Messaging;
using RutaSegura.BuildingBlocks.Application.Persistence;
using RutaSegura.BuildingBlocks.Application.Results;
using RutaSegura.BuildingBlocks.Application.Validation;

namespace RutaSegura.BuildingBlocks.Application.Behaviors;

// Cadena de decoradores (patrón Decorator) que envuelve a cada ICommandHandler:
//   Logging → Validation → Transaction → Handler

/// <summary>Registra duración y resultado de cada comando.</summary>
public sealed class LoggingCommandDecorator<TCommand, TResponse>(
    ICommandHandler<TCommand, TResponse> inner,
    ILogger<LoggingCommandDecorator<TCommand, TResponse>> logger)
    : ICommandHandler<TCommand, TResponse>
    where TCommand : ICommand<TResponse>
{
    public async Task<Result<TResponse>> HandleAsync(TCommand command, CancellationToken cancellationToken)
    {
        var name = typeof(TCommand).Name;
        var started = Stopwatch.GetTimestamp();
        try
        {
            var result = await inner.HandleAsync(command, cancellationToken);
            var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            if (result.IsSuccess)
                logger.LogInformation("{Command} OK en {ElapsedMs:0} ms", name, elapsed);
            else
                logger.LogWarning("{Command} rechazado ({Code}) en {ElapsedMs:0} ms", name, result.FirstError.Code, elapsed);
            return result;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "{Command} lanzó {Exception}", name, ex.GetType().Name);
            throw;
        }
    }
}

/// <summary>Ejecuta todos los IValidator&lt;TCommand&gt; antes del handler. Si hay errores, no toca el dominio.</summary>
public sealed class ValidationCommandDecorator<TCommand, TResponse>(
    ICommandHandler<TCommand, TResponse> inner,
    IEnumerable<IValidator<TCommand>> validators)
    : ICommandHandler<TCommand, TResponse>
    where TCommand : ICommand<TResponse>
{
    public Task<Result<TResponse>> HandleAsync(TCommand command, CancellationToken cancellationToken)
    {
        var errors = validators.SelectMany(v => v.Validate(command)).ToList();
        return errors.Count > 0
            ? Task.FromResult(Result<TResponse>.Failure(errors))
            : inner.HandleAsync(command, cancellationToken);
    }
}

/// <summary>
/// Confirma la unidad de trabajo (agregado + Outbox en la misma transacción) solo si el handler tuvo éxito.
/// Los handlers nunca llaman SaveChanges (R-01 de 5.1.4).
/// </summary>
public sealed class TransactionCommandDecorator<TCommand, TResponse>(
    ICommandHandler<TCommand, TResponse> inner,
    IUnitOfWork unitOfWork)
    : ICommandHandler<TCommand, TResponse>
    where TCommand : ICommand<TResponse>
{
    public async Task<Result<TResponse>> HandleAsync(TCommand command, CancellationToken cancellationToken)
    {
        var result = await inner.HandleAsync(command, cancellationToken);
        if (result.IsSuccess)
            await unitOfWork.SaveChangesAsync(cancellationToken);
        return result;
    }
}
