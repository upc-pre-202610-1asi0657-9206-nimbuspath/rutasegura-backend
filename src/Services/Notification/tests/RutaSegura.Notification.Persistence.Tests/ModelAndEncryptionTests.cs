using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using RutaSegura.Notification.Infrastructure.Persistence.EntityFrameworkCore.Configuration;
using RutaSegura.Notification.Infrastructure.Security;

namespace RutaSegura.Notification.Persistence.Tests;

public sealed class ModelAndEncryptionTests
{
    [Fact]
    public void TokensRoundTrip_WithoutDeterministicCiphertext()
    {
        var protector = TestSecrets.Protector;
        const string token = "opaque-fcm-registration-token";
        var first = protector.Protect(token);
        var second = protector.Protect(token);
        first.ShouldNotBe(second);
        first.ShouldNotContain(token);
        protector.Unprotect(first).ShouldBe(token);
    }

    [Fact]
    public void TamperedCiphertextAndDifferentKey_AreRejected()
    {
        var ciphertext = TestSecrets.Protector.Protect("opaque-token");
        var bytes = Convert.FromBase64String(ciphertext[3..]);
        bytes[^1] ^= 1;
        Should.Throw<CryptographicException>(() => TestSecrets.Protector.Unprotect("v1:" + Convert.ToBase64String(bytes)));
        var other = new AesGcmPushTokenProtector(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
        Should.Throw<CryptographicException>(() => other.Unprotect(ciphertext));
    }

    [Theory]
    [InlineData(16)]
    [InlineData(24)]
    public void KeyMustHave256Bits(int bytes) =>
        Should.Throw<ArgumentException>(() => new AesGcmPushTokenProtector(Convert.ToBase64String(new byte[bytes])));

    [Fact]
    public void MySqlModelMatchesSnapshot_AndMigrationsContainAllSevenTables()
    {
        using var context = new NotificationDbContext(new DbContextOptionsBuilder<NotificationDbContext>()
            .UseMySQL("Server=127.0.0.1;Database=notification_model_only;UserID=unused;Password=unused").Options, TestSecrets.Protector);
        context.Database.HasPendingModelChanges().ShouldBeFalse();
        context.Database.GetMigrations().Count().ShouldBe(2);
        var sql = context.Database.GenerateCreateScript();
        foreach (var table in new[] { "notification_notifications", "notification_announcements", "notification_push_subscriptions",
            "notification_processed_events", "notification_dispatch_jobs", "notification_deliveries", "notification_delivery_replays" }) sql.ShouldContain(table);
        sql.ShouldContain("token_ciphertext");
        sql.ShouldContain("ux_notification_source_recipient_subject");
        sql.ShouldContain("ux_processed_consumer_message");
        sql.ShouldContain("ck_notification_subject");
        sql.ShouldNotContain("opaque-token");
    }
}
