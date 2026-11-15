using System.Text.Json;
using RutaSegura.BuildingBlocks.Application.Results;
using RutaSegura.BuildingBlocks.Domain;
using RutaSegura.BuildingBlocks.Messaging;
using RutaSegura.BuildingBlocks.Testing;

namespace RutaSegura.BuildingBlocks.Foundation.Tests;

public sealed class FoundationTests
{
    private sealed record SampleEvent(DateTimeOffset OccurredOn) : DomainEvent(OccurredOn);
    private sealed class SampleAggregate : AggregateRoot<Guid>
    {
        public SampleAggregate() => Id = Guid.NewGuid();
        public void Change(DateTimeOffset at) => Raise(new SampleEvent(at));
    }
    public sealed record SamplePayload(Guid Id);

    [Fact]
    public void Aggregate_CollectsAndClearsInternalEvents()
    {
        var aggregate = new SampleAggregate();
        aggregate.Change(DateTimeOffset.UtcNow);
        var @event = Assert.Single(aggregate.DomainEvents);
        Assert.NotEqual(Guid.Empty, @event.EventId);
        aggregate.ClearDomainEvents();
        Assert.Empty(aggregate.DomainEvents);
    }

    [Fact]
    public void Result_CopiesErrorsSoCallerCannotTurnFailureIntoSuccess()
    {
        var errors = new List<Error> { Error.Conflict("DUPLICATE", "Already exists.") };
        var result = Result<string>.Failure(errors);
        errors.Clear();
        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Conflict, result.FirstError.Type);
        Assert.Throws<InvalidOperationException>(() => result.Value);
        Assert.Throws<ArgumentException>(() => Result<string>.Failure(Array.Empty<Error>()));
    }

    [Fact]
    public void AuthenticationAndAuthorizationErrorsAreDistinct()
    {
        Assert.NotEqual(Error.Unauthorized("INVALID_TOKEN", "Invalid.").Type,
            Error.Forbidden("DENIED", "Denied.").Type);
    }

    [Fact]
    public void Message_JsonRoundTripPreservesIdentityVersionAndUtcTimestamp()
    {
        var original = new IntegrationMessage<SamplePayload>(Guid.NewGuid(), "sample.created", 1,
            new DateTimeOffset(2026, 10, 5, 10, 0, 0, TimeSpan.FromHours(-5)), "request-1",
            new SamplePayload(Guid.NewGuid()));
        var copy = JsonSerializer.Deserialize<IntegrationMessage<SamplePayload>>(JsonSerializer.Serialize(original));
        Assert.Equal(original, copy);
        Assert.Equal(TimeSpan.Zero, copy!.OccurredAt.Offset);
        Assert.Equal(original.MessageId, copy.MessageId);
    }

    [Fact]
    public void Message_RejectsMissingIdentityVersionOrPayload()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.Throws<ArgumentException>(() => new IntegrationMessage<string>(Guid.Empty, "sample", 1, now, "c", "value"));
        Assert.Throws<ArgumentOutOfRangeException>(() => new IntegrationMessage<string>(Guid.NewGuid(), "sample", 0, now, "c", "value"));
        Assert.Throws<ArgumentException>(() => new IntegrationMessage<string>(Guid.NewGuid(), " ", 1, now, "c", "value"));
        Assert.Throws<ArgumentException>(() => new IntegrationMessage<string>(Guid.NewGuid(), "sample", 1, now, " ", "value"));
        Assert.Throws<ArgumentException>(() => new IntegrationMessage<string>(Guid.NewGuid(), "sample", 1, default, "c", "value"));
        Assert.Throws<ArgumentNullException>(() => new IntegrationMessage<string>(Guid.NewGuid(), "sample", 1, now, "c", null!));
    }

    [Fact]
    public async Task TestingPublisher_PreservesReplayIdentityWithoutPretendingToDeduplicate()
    {
        var publisher = new RecordingMessagePublisher();
        var message = new IntegrationMessage<string>(Guid.NewGuid(), "sample", 1, DateTimeOffset.UtcNow, "c", "payload");
        await publisher.PublishAsync(message, TestContext.Current.CancellationToken);
        await publisher.PublishAsync(message, TestContext.Current.CancellationToken);
        Assert.Equal(2, publisher.Messages.Count);
        Assert.All(publisher.Messages, m => Assert.Equal(message.MessageId, m.MessageId));
    }

    [Fact]
    public async Task TestingDoubles_HonorCancellationAndFailedCommit()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var unitOfWork = new RecordingUnitOfWork();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => unitOfWork.SaveChangesAsync(cancelled.Token));
        Assert.Equal(0, unitOfWork.Commits);
        var failed = new RecordingUnitOfWork(_ => throw new InvalidOperationException("Simulated storage failure."));
        await Assert.ThrowsAsync<InvalidOperationException>(() => failed.SaveChangesAsync(TestContext.Current.CancellationToken));
        Assert.Equal(0, failed.Commits);
        var publisher = new RecordingMessagePublisher();
        var message = new IntegrationMessage<string>(Guid.NewGuid(), "sample", 1, DateTimeOffset.UtcNow, "c", "payload");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => publisher.PublishAsync(message, cancelled.Token));
        Assert.Empty(publisher.Messages);
    }
}
