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
}
