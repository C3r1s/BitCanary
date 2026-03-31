using System.Security.Cryptography;

namespace Messenger.Client.Avalonia.Services.Crypto;

/// <summary>
/// Detects identity key changes using constant-time comparison (CryptographicOperations.FixedTimeEquals).
/// Returns false when stored IK is null — first contact is never a false-positive alert (Pitfall 3).
/// Plan 04-03 wires this into SignalProtocolEncryptionService and the UI notification path.
/// </summary>
public sealed class IdentityKeyChangeDetector : IIdentityKeyChangeDetector
{
    /// <inheritdoc/>
    public bool HasKeyChanged(byte[]? storedIk, byte[] incomingIk)
    {
        // Null stored IK means no previous key was recorded — treat as "no change" (Pitfall 3)
        if (storedIk is null)
            return false;

        // Use constant-time comparison to prevent timing oracle attacks (Pitfall 6)
        return !CryptographicOperations.FixedTimeEquals(storedIk, incomingIk);
    }
}
