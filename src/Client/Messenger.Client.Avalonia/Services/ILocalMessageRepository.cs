using Messenger.Shared.Contracts.Dtos;

namespace Messenger.Client.Avalonia.Services;

/// <summary>Persistent local storage for messages and chat metadata (SQLite-backed).</summary>
public interface ILocalMessageRepository
{
    // ── Messages ──────────────────────────────────────────────────────────
    Task SaveMessageAsync(MessageDto message, int protocolVersion = 0, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MessageDto>> GetMessagesAsync(Guid chatId, CancellationToken cancellationToken = default);
    Task<bool> MessageExistsAsync(Guid clientMessageId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists decrypted plaintext to messages.plaintext_body for FTS5 indexing.
    /// Idempotent: the UPDATE is guarded by <c>AND (plaintext_body IS NULL OR plaintext_body = '')</c>
    /// so already-indexed rows are not touched and the FTS5 update trigger does not fire again.
    /// </summary>
    Task UpdatePlaintextBodyAsync(Guid messageId, string plaintextBody, CancellationToken ct = default);

    // ── Chats ─────────────────────────────────────────────────────────────
    Task UpsertChatAsync(ChatSummaryDto chat, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ChatSummaryDto>> GetChatsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Resets the unread message count to 0 for the specified chat.
    /// Called when a chat is opened so the badge disappears (per D-07).
    /// </summary>
    Task ResetUnreadCountAsync(Guid chatId, CancellationToken cancellationToken = default);
}
