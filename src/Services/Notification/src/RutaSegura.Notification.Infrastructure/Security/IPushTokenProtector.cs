namespace RutaSegura.Notification.Infrastructure.Security;

public interface IPushTokenProtector
{
    string Protect(string token);
    string Unprotect(string ciphertext);
}
