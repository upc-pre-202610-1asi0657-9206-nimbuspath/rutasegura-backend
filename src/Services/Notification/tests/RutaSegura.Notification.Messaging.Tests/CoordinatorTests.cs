using Microsoft.Extensions.Logging.Abstractions;
using RutaSegura.Notification.Infrastructure.Messaging.Inbound;
using RutaSegura.Notification.Infrastructure.Messaging.RabbitMq;

namespace RutaSegura.Notification.Messaging.Tests;

public sealed class CoordinatorTests
{
    [Fact]
    public async Task DoesNotAckBeforeTheCommandCommitCompletes()
    {
        var commit = new TaskCompletionSource<NotificationConsumeResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var actions = new Actions();
        var running = Coordinator((_, _) => commit.Task).HandleAsync(MessageSamples.Message(), actions, TestContext.Current.CancellationToken);
        actions.Calls.ShouldBeEmpty();
        commit.SetResult(new(NotificationConsumeOutcome.Committed, "ok"));
        await running;
        actions.Calls.ShouldBe(["ack"]);
    }

    [Theory]
    [InlineData(NotificationConsumeOutcome.Retry, 0, "retry:1")]
    [InlineData(NotificationConsumeOutcome.Retry, 3, "dlq")]
    [InlineData(NotificationConsumeOutcome.Rejected, 0, "dlq")]
    public async Task ConfirmsTransferBeforeAcknowledgingOriginal(NotificationConsumeOutcome outcome, int previousAttempts, string destination)
    {
        var input = MessageSamples.Message();
        input.Headers![TripTrackingMessageDecoder.RetryHeader] = previousAttempts;
        var actions = new Actions();
        await Coordinator((_, _) => Task.FromResult(new NotificationConsumeResult(outcome, "failure")))
            .HandleAsync(input, actions, TestContext.Current.CancellationToken);
        actions.Calls.ShouldBe([destination, "ack"]);
    }

    [Fact]
    public async Task FailedPublisherConfirmLeavesOriginalUnacknowledged()
    {
        var actions = new Actions { FailForward = true };
        await Should.ThrowAsync<IOException>(() => Coordinator((_, _) => Task.FromResult(new NotificationConsumeResult(NotificationConsumeOutcome.Retry, "error")))
            .HandleAsync(MessageSamples.Message(), actions, TestContext.Current.CancellationToken));
        actions.Calls.ShouldBe(["retry:1"]);
    }

    [Fact]
    public async Task LostAckPropagatesSoWorkerClosesChannelAndRedelivers()
    {
        var actions = new Actions { FailAck = true };
        await Should.ThrowAsync<IOException>(() => Coordinator((_, _) => Task.FromResult(new NotificationConsumeResult(NotificationConsumeOutcome.Committed, "ok")))
            .HandleAsync(MessageSamples.Message(), actions, TestContext.Current.CancellationToken));
        actions.Calls.ShouldBe(["ack"]);
    }

    [Fact]
    public async Task CancellationWhileProcessingDoesNotAckOrDeadLetter()
    {
        var actions = new Actions();
        await Should.ThrowAsync<OperationCanceledException>(() => Coordinator((_, _) => throw new OperationCanceledException())
            .HandleAsync(MessageSamples.Message(), actions, TestContext.Current.CancellationToken));
        actions.Calls.ShouldBeEmpty();
    }

    private static NotificationDeliveryCoordinator Coordinator(Func<NotificationIncomingMessage, CancellationToken, Task<NotificationConsumeResult>> process) =>
        new(new Processor(process), new(), NullLogger<NotificationDeliveryCoordinator>.Instance);

    private sealed class Processor(Func<NotificationIncomingMessage, CancellationToken, Task<NotificationConsumeResult>> process) : INotificationMessageProcessor
    {
        public Task<NotificationConsumeResult> ProcessAsync(NotificationIncomingMessage message, CancellationToken cancellationToken) => process(message, cancellationToken);
    }
    private sealed class Actions : INotificationDeliveryActions
    {
        public List<string> Calls { get; } = [];
        public bool FailForward { get; init; }
        public bool FailAck { get; init; }
        public Task AcknowledgeAsync(CancellationToken cancellationToken)
        {
            Calls.Add("ack");
            return FailAck ? Task.FromException(new IOException("Lost ACK")) : Task.CompletedTask;
        }
        public Task ForwardAsync(int? retryAttempt, string errorCode, CancellationToken cancellationToken)
        {
            Calls.Add(retryAttempt is null ? "dlq" : "retry:" + retryAttempt);
            return FailForward ? Task.FromException(new IOException("No confirm")) : Task.CompletedTask;
        }
    }
}
