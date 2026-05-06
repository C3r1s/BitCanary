// Клиентское E2E: «IIdentityKeyChangeDetector» (сессии, ключи, ratchet).
namespace Messenger.Client.Avalonia.Services.Crypto;

public interface IIdentityKeyChangeDetector
{
    bool HasKeyChanged(byte[]? storedIk, byte[] incomingIk);

    event Func<string, byte[], Task>? IdentityKeyChanged;

    Task RaiseAsync(string sessionId, byte[] newIkPublic);
}
