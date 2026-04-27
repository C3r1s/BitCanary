using Messenger.Shared.Contracts;
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

    /// <summary>
    /// Updates the send status of a single message (by its client_message_id / server id).
    /// </summary>
    Task UpdateMessageStatusAsync(Guid messageId, MessageStatus status, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks all outgoing messages in the chat as Read for the current user.
    /// </summary>
    Task MarkMessagesReadAsync(Guid chatId, Guid currentUserId, CancellationToken cancellationToken = default);

    /// <summary>Deletes all messages then the chat row for chatId scoped to the current owner. Messages first — FK constraint.</summary>
    Task DeleteChatAsync(Guid chatId, CancellationToken ct = default);

    /// <summary>Deletes all messages in chatId but leaves the chat row intact.</summary>
    Task ClearMessagesAsync(Guid chatId, CancellationToken ct = default);
}
