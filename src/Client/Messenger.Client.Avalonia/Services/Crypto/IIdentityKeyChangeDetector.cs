namespace Messenger.Client.Avalonia.Services.Crypto;

/// <summary>
/// Detects whether a contact's identity key has changed since last session establishment.
/// Null stored IK always returns false (no false-positive alert on first contact).
/// Full implementation wired in Plan 04-03.
/// </summary>
public interface IIdentityKeyChangeDetector
{
    /// <summary>
    /// Returns true if the stored IK is non-null and differs from the incoming IK.
    /// Returns false if storedIk is null (first contact) or if the keys match.
    /// </summary>
    bool HasKeyChanged(byte[]? storedIk, byte[] incomingIk);
}
