// Сервис клиента BitCanary: сеть, кэш, медиа — «DpapiKeyStore».
using Microsoft.AspNetCore.DataProtection;

namespace Messenger.Client.Avalonia.Services;

public sealed class DpapiKeyStore(IDataProtectionProvider dpProvider) : IKeyStore
{
    private readonly IDataProtector _keyProtector =
        dpProvider.CreateProtector("Messenger.IdentityKey.v1");

    public byte[] Protect(byte[] rawKeyBytes) => _keyProtector.Protect(rawKeyBytes);
    public byte[] Unprotect(byte[] protectedBlob) => _keyProtector.Unprotect(protectedBlob);
    public string ProtectToBase64(byte[] rawKeyBytes) => Convert.ToBase64String(Protect(rawKeyBytes));
    public byte[] UnprotectFromBase64(string base64Blob) => Unprotect(Convert.FromBase64String(base64Blob));
}
