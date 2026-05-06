// Клиентское E2E: «IdentityKeyChangeDetector» (сессии, ключи, ratchet).
using System.Security.Cryptography;

namespace Messenger.Client.Avalonia.Services.Crypto;

public sealed class IdentityKeyChangeDetector : IIdentityKeyChangeDetector
{
    public event Func<string, byte[], Task>? IdentityKeyChanged;

    public bool HasKeyChanged(byte[]? storedIk, byte[] incomingIk)
    {
        if (storedIk is null)
            return false;

        return !CryptographicOperations.FixedTimeEquals(storedIk, incomingIk);
    }

    public async Task RaiseAsync(string sessionId, byte[] newIkPublic)
    {
        var handler = IdentityKeyChanged;
        if (handler is null)
            return;

        foreach (var subscriber in handler.GetInvocationList().Cast<Func<string, byte[], Task>>())
        {
            await subscriber(sessionId, newIkPublic);
        }
    }
}
