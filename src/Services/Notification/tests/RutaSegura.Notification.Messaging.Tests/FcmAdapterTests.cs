using FirebaseAdmin.Messaging;
using RutaSegura.Notification.Application.Model.Results;
using RutaSegura.Notification.Infrastructure.Channels;
using RutaSegura.Notification.Infrastructure.Channels.Fcm;

namespace RutaSegura.Notification.Messaging.Tests;

public sealed class FcmAdapterTests
{
    [Fact]
    public async Task BuildsGenericHintAndDoesNotSendPersonalContentOrGps()
    {
        var transport = new Transport();
        var sender = new FcmPushNotificationSender(transport, new() { Enabled = true });
        var result = await sender.SendAsync(Notice(), "opaque-registration-token", TestContext.Current.CancellationToken);
        result.Accepted.ShouldBeTrue(); result.ProviderMessageId.ShouldBe("projects/test/messages/test");
        transport.Message!.Data.Keys.ShouldBe(["notificationId"]);
        transport.Message.Notification.Title.ShouldBe("RutaSegura");
        transport.Message.Notification.Body.ShouldNotContain("CONTENIDO PRIVADO");
        transport.Message.Webpush.Headers["TTL"].ShouldBe("60");
    }

    [Theory]
    [InlineData(MessagingErrorCode.Unregistered, false, "FCM_UNREGISTERED")]
    [InlineData(MessagingErrorCode.SenderIdMismatch, false, "FCM_SENDER_ID_MISMATCH")]
    [InlineData(MessagingErrorCode.InvalidArgument, false, "FCM_INVALID_ARGUMENT")]
    [InlineData(MessagingErrorCode.QuotaExceeded, true, "FCM_QUOTA_EXCEEDED")]
    [InlineData(MessagingErrorCode.Unavailable, true, "FCM_UNAVAILABLE")]
    [InlineData(MessagingErrorCode.Internal, true, "FCM_INTERNAL")]
    public void ProviderFailuresHaveStableCodesAndRetryClassification(MessagingErrorCode error, bool retryable, string code)
    {
        var result = FcmPushNotificationSender.Classify(error);
        result.Retryable.ShouldBe(retryable); result.ErrorCode.ShouldBe(code); result.Accepted.ShouldBeFalse();
    }

    [Fact]
    public async Task MissingProviderAndTimeoutNeverPretendSuccessfulDelivery()
    {
        var sender = new FcmPushNotificationSender(new Transport { Failure = true }, new() { Enabled = true });
        var result = await sender.SendAsync(Notice(), "secret-token", TestContext.Current.CancellationToken);
        result.Accepted.ShouldBeFalse(); result.Retryable.ShouldBeTrue(); result.ErrorCode.ShouldBe("FCM_UNAVAILABLE");
    }

    [Fact]
    public async Task DisabledFcmDoesNotTouchCredentialsOrNetwork()
    {
        var transport = new Transport();
        var result = await new FcmPushNotificationSender(transport, new()).SendAsync(Notice(), "secret", TestContext.Current.CancellationToken);
        result.Accepted.ShouldBeFalse(); transport.Message.ShouldBeNull();
    }

    private static NotificationResult Notice() => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
        "Boarding", "Normal", "TITULO PRIVADO", "CONTENIDO PRIVADO", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null);
    private sealed class Transport : IFcmTransport
    {
        public Message? Message { get; private set; }
        public bool Failure { get; init; }
        public Task<string> SendAsync(Message message, CancellationToken cancellationToken)
        {
            Message = message;
            return Failure ? Task.FromException<string>(new IOException("detalle sensible no propagable")) : Task.FromResult("projects/test/messages/test");
        }
    }
}
