// Сервис клиента BitCanary: сеть, кэш, медиа — «IKeyStore».
namespace Messenger.Client.Avalonia.Services;

public interface IKeyStore
{
    byte[] Protect(byte[] rawKeyBytes);
    byte[] Unprotect(byte[] protectedBlob);
    string ProtectToBase64(byte[] rawKeyBytes);
    byte[] UnprotectFromBase64(string base64Blob);
}
