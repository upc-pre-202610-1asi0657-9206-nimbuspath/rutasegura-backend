using Microsoft.Extensions.DependencyInjection;
using RutaSegura.BuildingBlocks.Application.DependencyInjection;
using RutaSegura.BuildingBlocks.Application.Messaging;
using RutaSegura.BuildingBlocks.Application.Persistence;
using RutaSegura.BuildingBlocks.Application.Results;
using RutaSegura.BuildingBlocks.Testing;

namespace RutaSegura.BuildingBlocks.Application.Tests;

public sealed record CrashCommand : ICommand<string>;
internal sealed class CrashCommandHandler : ICommandHandler<CrashCommand, string>
{
    public Task<Result<string>> HandleAsync(CrashCommand command, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("Simulated handler failure.");
}

public sealed class PipelineFailureTests
{
    private static ServiceProvider Build(RecordingUnitOfWork unitOfWork) =>
        new ServiceCollection().AddLogging().AddSingleton<IUnitOfWork>(unitOfWork)
            .AddRutaSeguraApplication(typeof(PipelineFailureTests).Assembly)
            .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

    [Fact]
    public async Task HandlerException_DoesNotCommit()
    {
        var unitOfWork = new RecordingUnitOfWork();
        using var provider = Build(unitOfWork);
        using var scope = provider.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            dispatcher.SendAsync(new CrashCommand(), TestContext.Current.CancellationToken));
        Assert.Equal(0, unitOfWork.Commits);
    }

    [Fact]
    public async Task BusinessConflictThroughDispatcher_DoesNotCommit()
    {
        var unitOfWork = new RecordingUnitOfWork();
        using var provider = Build(unitOfWork);
        using var scope = provider.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<IDispatcher>()
            .SendAsync(new RenameCommand("conflict"), TestContext.Current.CancellationToken);
        Assert.Equal(ErrorType.Conflict, result.FirstError.Type);
        Assert.Equal(0, unitOfWork.Commits);
    }

    [Fact]
    public async Task PersistenceException_IsPropagatedAndNotReportedAsSuccess()
    {
        var unitOfWork = new RecordingUnitOfWork(_ => throw new InvalidOperationException("Simulated commit failure."));
        using var provider = Build(unitOfWork);
        using var scope = provider.CreateScope();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            scope.ServiceProvider.GetRequiredService<IDispatcher>()
                .SendAsync(new RenameCommand("valid"), TestContext.Current.CancellationToken));
        Assert.Equal(0, unitOfWork.Commits);
    }
}
