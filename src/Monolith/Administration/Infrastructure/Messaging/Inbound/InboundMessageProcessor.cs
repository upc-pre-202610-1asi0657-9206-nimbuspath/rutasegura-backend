using Microsoft.EntityFrameworkCore;
using Monolith.Administration.Application.CommandServices;
using Monolith.Administration.Domain.Model.Commands;
using Monolith.Shared.Domain.Repositories;
using Monolith.Shared.Infrastructure.Persistence.EFC.Configuration;

namespace Monolith.Administration.Infrastructure.Messaging.Inbound;

public enum InboundOutcome
{
    Processed,
    Duplicate,
    Ignored,
    Rejected,
    Retry
}

/// <summary>
///     Handles one message from <c>administration.inbound</c> without any AMQP dependency (so it is tested directly):
///     dedupe by message id (Inbox), translate, run the use case and save the Inbox row in the same commit.
/// </summary>
public class InboundMessageProcessor(
    AppDbContext context,
    IOperationsMonitoringCommandService monitoring,
    IUnitOfWork unitOfWork,
    TimeProvider clock,
    ILogger<InboundMessageProcessor> logger)
{
    public async Task<InboundOutcome> HandleAsync(string? messageIdText, string messageType,
        ReadOnlyMemory<byte> body, bool redelivered, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(messageIdText, out var messageId) || messageId == Guid.Empty)
        {
            logger.LogWarning("Message {Type} without a valid message id: dead-lettered", messageType);
            return InboundOutcome.Rejected;
        }

        object? command;
        try
        {
            command = TripTrackingMessageTranslator.Translate(messageType, body.Span);
        }
        catch (InvalidInboundMessageException ex)
        {
            logger.LogWarning(ex, "Message {MessageId} ({Type}) is invalid: dead-lettered", messageId, messageType);
            return InboundOutcome.Rejected;
        }

        if (command is null) return InboundOutcome.Ignored;

        try
        {
            if (await context.Set<InboxMessage>().AsNoTracking()
                    .AnyAsync(m => m.MessageId == messageId, cancellationToken))
                return InboundOutcome.Duplicate;

            context.Set<InboxMessage>().Add(new InboxMessage
            {
                MessageId = messageId, MessageType = messageType, ProcessedAt = clock.GetUtcNow()
            });

            var (succeeded, error, message) = command switch
            {
                RecordIncidentReportedCommand incident => Outcome(await monitoring.Handle(incident, cancellationToken)),
                RecordSosRaisedCommand sos => Outcome(await monitoring.Handle(sos, cancellationToken)),
                RecordSosResolvedCommand resolved => Outcome(await monitoring.Handle(resolved, cancellationToken)),
                RecordTripCompletedCommand completed => Outcome(await monitoring.Handle(completed, cancellationToken)),
                _ => (false, (Enum?)null, $"No handler for {command.GetType().Name}.")
            };

            if (!succeeded)
            {
                logger.LogWarning("Message {MessageId} ({Type}) rejected: {Error} {Message}", messageId, messageType,
                    error, message);
                return InboundOutcome.Rejected;
            }

            // Idempotent use cases may return without committing: the Inbox row is saved anyway.
            await unitOfWork.CompleteAsync(cancellationToken);
            return InboundOutcome.Processed;
        }
        catch (DbUpdateException ex) when (IsDuplicateInbox(ex))
        {
            return InboundOutcome.Duplicate; // another instance processed it at the same time
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Error processing {MessageId} ({Type}); {Action}", messageId, messageType,
                redelivered ? "second failure, dead-lettered" : "it will be retried once");
            return redelivered ? InboundOutcome.Rejected : InboundOutcome.Retry;
        }
    }

    private static (bool, Enum?, string) Outcome<T>(Monolith.Shared.Application.Model.Result<T> result) =>
        (result.IsSuccess, result.Error, result.Message);

    private static bool IsDuplicateInbox(DbUpdateException ex) =>
        ex.InnerException?.Message.Contains("Duplicate", StringComparison.OrdinalIgnoreCase) == true ||
        ex.InnerException?.Message.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase) == true;
}
