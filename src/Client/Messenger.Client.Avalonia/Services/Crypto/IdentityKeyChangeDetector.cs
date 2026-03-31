using System.Security.Cryptography;

namespace Messenger.Client.Avalonia.Services.Crypto;

/// <summary>
/// Detects identity key changes using constant-time comparison (CryptographicOperations.FixedTimeEquals).
/// Returns false when stored IK is null — first contact is never a false-positive alert (Pitfall 3).
/// Exposes an event for the decryption pipeline to notify the UI when a change is detected.
/// </summary>
public sealed class IdentityKeyChangeDetector : IIdentityKeyChangeDetector
{
    /// <inheritdoc/>
    public event Func<string, byte[], Task>? IdentityKeyChanged;

    /// <inheritdoc/>
    public bool HasKeyChanged(byte[]? storedIk, byte[] incomingIk)
    {
        // Null stored IK means no previous key was recorded — treat as "no change" (Pitfall 3)
        if (storedIk is null)
            return false;

        // Use constant-time comparison to prevent timing oracle attacks (Pitfall 6)
        return !CryptographicOperations.FixedTimeEquals(storedIk, incomingIk);
    }

    /// <inheritdoc/>
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
