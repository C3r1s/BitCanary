namespace Messenger.Client.Avalonia.Services.Crypto;

/// <summary>
/// SQLite CRUD for the ratchet_sessions and skipped_message_keys tables.
/// </summary>
public interface IRatchetSessionRepository
{
    Task<byte[]?> LoadSessionAsync(string sessionId, CancellationToken ct = default);
    Task SaveSessionAsync(string sessionId, byte[] blob, CancellationToken ct = default);

    Task<byte[]?> TryConsumeSkippedKeyAsync(
        string sessionId,
        byte[] ratchetPubkey,
        int messageNumber,
        CancellationToken ct = default);

    Task SaveSkippedKeyAsync(
        string sessionId,
        byte[] ratchetPubkey,
        int messageNumber,
        byte[] messageKey,
        CancellationToken ct = default);

    Task EnforceSkippedKeyLimitAsync(string sessionId, int maxKeys, CancellationToken ct = default);

    /// <summary>
    /// Loads the verification state for a session.
    /// Returns (false, null, null) if the session does not exist — no false-positive alerts.
    /// </summary>
    Task<(bool Verified, DateTimeOffset? LastVerifiedAt, byte[]? RemoteIkPublic)>
        LoadVerificationStateAsync(string sessionId, CancellationToken ct = default);

    /// <summary>
    /// Persists the verification state for a session.
    /// Creates the session row if it does not yet exist (UPSERT pattern).
    /// </summary>
    Task SaveVerificationStateAsync(
        string sessionId,
        bool verified,
        DateTimeOffset? lastVerifiedAt,
        byte[]? remoteIkPublic,
        CancellationToken ct = default);
}
