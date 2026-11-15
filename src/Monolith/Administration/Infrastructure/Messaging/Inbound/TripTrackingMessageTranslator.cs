using System.Text.Json;
using Monolith.Administration.Application.IntegrationEvents.Inbound;

namespace Monolith.Administration.Infrastructure.Messaging.Inbound;

/// <summary>
///     Anti-Corruption Layer for incoming messages: JSON of a Trip &amp; Tracking contract → Administration command.
///     Messages are recognized by the suffix of their type, like Trip &amp; Tracking does with ours.
/// </summary>
public static class TripTrackingMessageTranslator
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>
    ///     Returns the command for the message, <c>null</c> when the type is not relevant for Administration, or throws
    ///     <see cref="InvalidInboundMessageException" /> when the body cannot be read (the message goes to the DLQ).
    /// </summary>
    public static object? Translate(string messageType, ReadOnlySpan<byte> body)
    {
        var suffix = messageType.Contains('.') ? messageType[(messageType.LastIndexOf('.') + 1)..] : messageType;
        try
        {
            return suffix switch
            {
                "incident-reported" => Read<IncidentReportedV1>(body).ToCommand(),
                "sos-raised" => Read<SosRaisedV1>(body).ToCommand(),
                "sos-resolved" => Read<SosResolvedV1>(body).ToCommand(),
                "trip-completed" => Read<TripCompletedV1>(body).ToCommand(),
                _ => null
            };
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or ArgumentException)
        {
            throw new InvalidInboundMessageException($"Invalid body for {messageType}: {ex.Message}", ex);
        }
    }

    private static T Read<T>(ReadOnlySpan<byte> body) =>
        JsonSerializer.Deserialize<T>(body, Json) ?? throw new JsonException("Empty body.");
}

public sealed class InvalidInboundMessageException(string message, Exception? inner = null)
    : Exception(message, inner);
