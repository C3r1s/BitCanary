// Клиентское E2E: «SessionManager» (сессии, ключи, ratchet).
using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace Messenger.Client.Avalonia.Services.Crypto;

public sealed class SessionManager(
    IDoubleRatchetService ratchet,
    IRatchetSessionRepository repo) : ISessionManager
{
    private const int MaxSkippedKeys = 2000;

    private readonly ConcurrentDictionary<string, SemaphoreSlim> _sessionLocks = new();

    private readonly ConcurrentDictionary<string, RatchetState> _stateCache = new();


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

    public async Task SaveSessionAsync(RatchetState state, CancellationToken ct = default)
    {
        var blob = state.Serialize();
        await repo.SaveSessionAsync(state.SessionId, blob, ct);
        _stateCache[state.SessionId] = state;
    }

    public async Task ResetSessionAsync(string sessionId, CancellationToken ct = default)
    {
        var sem = _sessionLocks.GetOrAdd(sessionId, _ => new SemaphoreSlim(1, 1));
        await sem.WaitAsync(ct);
        try
        {
            await repo.DeleteSessionAsync(sessionId, ct);
            _stateCache.TryRemove(sessionId, out _);
        }
        finally
        {
            sem.Release();
        }
    }

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
