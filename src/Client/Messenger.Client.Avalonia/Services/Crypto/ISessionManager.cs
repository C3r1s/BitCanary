namespace Messenger.Client.Avalonia.Services.Crypto;

/// <summary>
/// Thread-safe, persistent Double Ratchet session lifecycle.
/// Each session is guarded by a per-session SemaphoreSlim(1,1) and
/// its ratchet state is persisted to SQLite WAL after every encrypt/decrypt.
/// </summary>
public interface ISessionManager
{
    Task<RatchetState?> GetSessionAsync(string sessionId, CancellationToken ct = default);
    Task SaveSessionAsync(RatchetState state, CancellationToken ct = default);

    Task<(byte[] Ciphertext, byte[] RatchetPublic, int Pn, int N)> EncryptAsync(
        string sessionId, byte[] plaintext, byte[] associatedData, CancellationToken ct = default);

    Task<byte[]> DecryptAsync(
        string sessionId,
        byte[] ciphertext,
        byte[] ratchetPublic,
        int pn,
        int n,
        byte[] associatedData,
        CancellationToken ct = default);

    Task CreateInitiatorSessionAsync(
        string sessionId,
        byte[] sharedSecret,
        byte[] remoteSpkPublic,
        CancellationToken ct = default);

    Task CreateResponderSessionAsync(
        string sessionId,
        byte[] sharedSecret,
        byte[] ownSpkPrivate,
        byte[] ownSpkPublic,
        CancellationToken ct = default);
}
