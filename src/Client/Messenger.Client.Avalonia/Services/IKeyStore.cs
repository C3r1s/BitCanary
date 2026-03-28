namespace Messenger.Client.Avalonia.Services;

/// <summary>Protects and unprotects raw key material using DPAPI (CurrentUser scope).</summary>
public interface IKeyStore
{
    byte[] Protect(byte[] rawKeyBytes);
    byte[] Unprotect(byte[] protectedBlob);
    string ProtectToBase64(byte[] rawKeyBytes);
    byte[] UnprotectFromBase64(string base64Blob);
}
