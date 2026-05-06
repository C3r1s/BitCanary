// Клиентское E2E: «IRatchetSessionRepository» (сессии, ключи, ratchet).
namespace Messenger.Client.Avalonia.Services.Crypto;

public interface IRatchetSessionRepository
{
    Task<byte[]?> LoadSessionAsync(string sessionId, CancellationToken ct = default);
    Task SaveSessionAsync(string sessionId, byte[] blob, CancellationToken ct = default);
    Task DeleteSessionAsync(string sessionId, CancellationToken ct = default);

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

    Task<(bool Verified, DateTimeOffset? LastVerifiedAt, byte[]? RemoteIkPublic)>
        LoadVerificationStateAsync(string sessionId, CancellationToken ct = default);

    Task SaveVerificationStateAsync(
        string sessionId,
        bool verified,
        DateTimeOffset? lastVerifiedAt,
        byte[]? remoteIkPublic,
        CancellationToken ct = default);
}
