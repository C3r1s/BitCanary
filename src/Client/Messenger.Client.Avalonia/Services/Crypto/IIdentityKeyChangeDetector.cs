namespace Messenger.Client.Avalonia.Services.Crypto;

/// <summary>
/// Detects whether a contact's identity key has changed since last session establishment.
/// Null stored IK always returns false (no false-positive alert on first contact).
/// Provides both a synchronous HasKeyChanged helper for direct comparison and
/// an async event-based RaiseAsync for the decryption pipeline.
/// </summary>
public interface IIdentityKeyChangeDetector
{
    /// <summary>
    /// Returns true if the stored IK is non-null and differs from the incoming IK.
    /// Returns false if storedIk is null (first contact) or if the keys match.
    /// </summary>
    bool HasKeyChanged(byte[]? storedIk, byte[] incomingIk);

    /// <summary>
    /// Fired when an identity key change is detected during decryption.
    /// sessionId: "{senderId}:{recipientId}", newIkPublic: the new key bytes.
    /// </summary>
    event Func<string, byte[], Task>? IdentityKeyChanged;

    /// <summary>
    /// Raises the IdentityKeyChanged event and awaits all subscribers.
    /// Called by SignalProtocolEncryptionService when a key mismatch is detected.
    /// </summary>
    Task RaiseAsync(string sessionId, byte[] newIkPublic);
}
