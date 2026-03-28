using Messenger.Shared.Contracts.Dtos;

namespace Messenger.Client.Avalonia.Services;

/// <summary>Persistent local storage for messages and chat metadata (SQLite-backed).</summary>
public interface ILocalMessageRepository
{
    // ── Messages ──────────────────────────────────────────────────────────
    Task SaveMessageAsync(MessageDto message, int protocolVersion = 0, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MessageDto>> GetMessagesAsync(Guid chatId, CancellationToken cancellationToken = default);
    Task<bool> MessageExistsAsync(Guid clientMessageId, CancellationToken cancellationToken = default);

    // ── Chats ─────────────────────────────────────────────────────────────
    Task UpsertChatAsync(ChatSummaryDto chat, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ChatSummaryDto>> GetChatsAsync(CancellationToken cancellationToken = default);
}
