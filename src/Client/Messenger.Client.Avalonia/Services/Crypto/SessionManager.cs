using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace Messenger.Client.Avalonia.Services.Crypto;

/// <summary>
/// Thread-safe Double Ratchet session lifecycle manager.
///
/// Each session is protected by a dedicated SemaphoreSlim(1,1) to prevent
/// concurrent ratchet advances. State is persisted to SQLite WAL after
/// every encrypt and decrypt operation (D-11, D-12).
/// </summary>
public sealed class SessionManager(
    IDoubleRatchetService ratchet,
    IRatchetSessionRepository repo) : ISessionManager
{
    private const int MaxSkippedKeys = 2000;

    // Per-session concurrency locks (D-12)
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _sessionLocks = new();

    // In-memory cache: avoids a SQLite round-trip on every message
    private readonly ConcurrentDictionary<string, RatchetState> _stateCache = new();

    // -------------------------------------------------------------------------
    // ISessionManager implementation
    // -------------------------------------------------------------------------

    /// <inheritdoc/>
    public async Task<RatchetState?> GetSessionAsync(string sessionId, CancellationToken ct = default)
    {
        if (_stateCache.TryGetValue(sessionId, out var cached))
            return cached;

        var blob = await repo.LoadSessionAsync(sessionId, ct);
        if (blob is null)
            return null;

        var state = RatchetState.Deserialize(blob);
        _stateCache[sessionId] = state;
        return state;
    }

    /// <inheritdoc/>
    public async Task SaveSessionAsync(RatchetState state, CancellationToken ct = default)
    {
        var blob = state.Serialize();
        await repo.SaveSessionAsync(state.SessionId, blob, ct);
        _stateCache[state.SessionId] = state;
    }

    /// <inheritdoc/>
    public async Task<(byte[] Ciphertext, byte[] RatchetPublic, int Pn, int N)> EncryptAsync(
        string sessionId,
        byte[] plaintext,
        byte[] associatedData,
        CancellationToken ct = default)
    {
        var sem = _sessionLocks.GetOrAdd(sessionId, _ => new SemaphoreSlim(1, 1));
        await sem.WaitAsync(ct);
        try
        {
            var state = await GetSessionAsync(sessionId, ct)
                ?? throw new CryptographicException($"Session '{sessionId}' not found. Call CreateInitiatorSessionAsync first.");

            var (ciphertext, ratchetPublic, pn, n) = ratchet.Encrypt(state, plaintext, associatedData);
            await SaveSessionAsync(state, ct);

            return (ciphertext, ratchetPublic, pn, n);
        }
        finally
        {
            sem.Release();
        }
    }

    /// <inheritdoc/>
    public async Task<byte[]> DecryptAsync(
        string sessionId,
        byte[] ciphertext,
        byte[] ratchetPublic,
        int pn,
        int n,
        byte[] associatedData,
        CancellationToken ct = default)
    {
        var sem = _sessionLocks.GetOrAdd(sessionId, _ => new SemaphoreSlim(1, 1));
        await sem.WaitAsync(ct);
        try
        {
            var state = await GetSessionAsync(sessionId, ct)
                ?? throw new CryptographicException($"Session '{sessionId}' not found.");

            // Skipped key delegates — use sync-over-async inside the semaphore
            // (acceptable because the semaphore serializes all access to this session)
            byte[]? TryConsumeSkippedKey(byte[] rpk, int mn) =>
                repo.TryConsumeSkippedKeyAsync(sessionId, rpk, mn, ct).GetAwaiter().GetResult();

            void StoreSkippedKey(byte[] rpk, int mn, byte[] mk)
            {
                repo.SaveSkippedKeyAsync(sessionId, rpk, mn, mk, ct).GetAwaiter().GetResult();
                repo.EnforceSkippedKeyLimitAsync(sessionId, MaxSkippedKeys, ct).GetAwaiter().GetResult();
            }

            var plaintext = ratchet.Decrypt(state, ciphertext, ratchetPublic, pn, n, associatedData,
                TryConsumeSkippedKey, StoreSkippedKey);

            await SaveSessionAsync(state, ct);
            return plaintext;
        }
        finally
        {
            sem.Release();
        }
    }

    /// <inheritdoc/>
    public async Task CreateInitiatorSessionAsync(
        string sessionId,
        byte[] sharedSecret,
        byte[] remoteSpkPublic,
        CancellationToken ct = default)
    {
        var state = ratchet.InitializeInitiator(sharedSecret, remoteSpkPublic);
        state.SessionId = sessionId;
        await SaveSessionAsync(state, ct);
    }

    /// <inheritdoc/>
    public async Task CreateResponderSessionAsync(
        string sessionId,
        byte[] sharedSecret,
        byte[] ownSpkPrivate,
        byte[] ownSpkPublic,
        CancellationToken ct = default)
    {
        var state = ratchet.InitializeResponder(sharedSecret, ownSpkPrivate, ownSpkPublic);
        state.SessionId = sessionId;
        await SaveSessionAsync(state, ct);
    }
}
