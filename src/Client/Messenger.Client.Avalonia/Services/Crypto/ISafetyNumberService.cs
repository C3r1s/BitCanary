namespace Messenger.Client.Avalonia.Services.Crypto;

/// <summary>
/// Computes a deterministic 60-digit safety number for a conversation.
/// Both parties produce identical output given the same inputs in any order.
/// </summary>
public interface ISafetyNumberService
{
    /// <summary>
    /// Computes the safety number for a conversation between two users.
    /// </summary>
    /// <param name="localIkPublic">Local party's 32-byte Ed25519 identity key public.</param>
    /// <param name="localUserId">Local party's user ID string (Guid "D" format).</param>
    /// <param name="remoteIkPublic">Remote party's 32-byte Ed25519 identity key public.</param>
    /// <param name="remoteUserId">Remote party's user ID string (Guid "D" format).</param>
    /// <returns>
    /// A 71-character string: 12 groups of 5 decimal digits separated by single spaces,
    /// e.g. "12345 67890 11111 22222 33333 44444 55555 66666 77777 88888 99999 00000".
    /// </returns>
    string Compute(byte[] localIkPublic, string localUserId,
                   byte[] remoteIkPublic, string remoteUserId);
}
