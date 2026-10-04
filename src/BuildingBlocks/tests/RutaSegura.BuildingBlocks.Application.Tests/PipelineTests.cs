using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RutaSegura.BuildingBlocks.Application.Behaviors;
using RutaSegura.BuildingBlocks.Application.DependencyInjection;
using RutaSegura.BuildingBlocks.Application.Messaging;
using RutaSegura.BuildingBlocks.Application.Persistence;
using RutaSegura.BuildingBlocks.Application.Results;
using RutaSegura.BuildingBlocks.Application.Validation;

namespace RutaSegura.BuildingBlocks.Application.Tests;

// ───── Comando de ejemplo que se descubre por escaneo de este mismo ensamblado ─────

public sealed record RenameCommand(string Name) : ICommand<string>;

internal sealed class RenameCommandValidator : IValidator<RenameCommand>
{
    public IEnumerable<Error> Validate(RenameCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.Name))
            yield return Error.Validation("NAME_REQUIRED", "Name is required");
    }
}

internal sealed class RenameCommandHandler : ICommandHandler<RenameCommand, string>
{
    public int Calls { get; private set; }

    public Task<Result<string>> HandleAsync(RenameCommand command, CancellationToken cancellationToken)
    {
        Calls++;
        return Task.FromResult<Result<string>>(command.Name == "conflict"
            ? Error.Conflict("NAME_TAKEN", "Name already taken")
            : command.Name.ToUpperInvariant());
    }
}

public sealed record PingQuery : IQuery<string>;

internal sealed class PingQueryHandler : IQueryHandler<PingQuery, string>
{
    public Task<Result<string>> HandleAsync(PingQuery query, CancellationToken cancellationToken) =>
        Task.FromResult<Result<string>>("pong");
}

internal sealed class CountingUnitOfWork : IUnitOfWork
{
    public int Commits { get; private set; }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        Commits++;
        return Task.CompletedTask;
    }
}

[Trait("Category", "Application")]
public class DecoratorTests
{
    [Fact]
    public async Task TransactionDecorator_SuccessfulCommand_CommitsOnce()
    {
        var uow = new CountingUnitOfWork();
        var sut = new TransactionCommandDecorator<RenameCommand, string>(new RenameCommandHandler(), uow);

        var result = await sut.HandleAsync(new RenameCommand("ruta 3"), TestContext.Current.CancellationToken);

        result.Value.ShouldBe("RUTA 3");
        uow.Commits.ShouldBe(1);
    }

    [Fact]
    public async Task TransactionDecorator_FailedCommand_DoesNotCommit()
    {
        var uow = new CountingUnitOfWork();
        var sut = new TransactionCommandDecorator<RenameCommand, string>(new RenameCommandHandler(), uow);

        var result = await sut.HandleAsync(new RenameCommand("conflict"), TestContext.Current.CancellationToken);

        result.IsFailure.ShouldBeTrue();
        uow.Commits.ShouldBe(0);
    }

    [Fact]
    public async Task ValidationDecorator_InvalidCommand_ShortCircuitsHandler()
    {
        var handler = new RenameCommandHandler();
        var sut = new ValidationCommandDecorator<RenameCommand, string>(handler, [new RenameCommandValidator()]);

        var result = await sut.HandleAsync(new RenameCommand(" "), TestContext.Current.CancellationToken);

        result.FirstError.Code.ShouldBe("NAME_REQUIRED");
        result.FirstError.Type.ShouldBe(ErrorType.Validation);
        handler.Calls.ShouldBe(0);
    }
}

[Trait("Category", "Application")]
public class DispatcherPipelineTests
{
    private static (IDispatcher Dispatcher, CountingUnitOfWork Uow) Build()
    {
        var uow = new CountingUnitOfWork();
        var services = new ServiceCollection()
            .AddLogging(b => b.SetMinimumLevel(LogLevel.Warning))
            .AddSingleton<IUnitOfWork>(uow)
            .AddRutaSeguraApplication(typeof(DispatcherPipelineTests).Assembly);
        var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        return (provider.CreateScope().ServiceProvider.GetRequiredService<IDispatcher>(), uow);
    }

    [Fact]
    public async Task SendAsync_ValidCommand_RunsThroughAllDecoratorsAndCommits()
    {
        var (dispatcher, uow) = Build();

        var result = await dispatcher.SendAsync(new RenameCommand("viaje"), TestContext.Current.CancellationToken);

        result.Value.ShouldBe("VIAJE");
        uow.Commits.ShouldBe(1);
    }

    [Fact]
    public async Task SendAsync_InvalidCommand_ReturnsValidationErrorAndDoesNotCommit()
    {
        var (dispatcher, uow) = Build();

        var result = await dispatcher.SendAsync(new RenameCommand(""), TestContext.Current.CancellationToken);

        result.FirstError.Code.ShouldBe("NAME_REQUIRED");
        uow.Commits.ShouldBe(0);
    }

    [Fact]
    public async Task QueryAsync_ResolvesQueryHandlerWithoutTransaction()
    {
        var (dispatcher, uow) = Build();

        var result = await dispatcher.QueryAsync(new PingQuery(), TestContext.Current.CancellationToken);

        result.Value.ShouldBe("pong");
        uow.Commits.ShouldBe(0);
    }
}
