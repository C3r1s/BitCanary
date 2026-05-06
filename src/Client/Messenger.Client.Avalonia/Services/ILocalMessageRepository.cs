// Сервис клиента BitCanary: сеть, кэш, медиа — «ILocalMessageRepository».
using Messenger.Shared.Contracts;
using Messenger.Shared.Contracts.Dtos;

namespace Messenger.Client.Avalonia.Services;

public interface ILocalMessageRepository
{
    Task SaveMessageAsync(MessageDto message, int protocolVersion = 0, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MessageDto>> GetMessagesAsync(Guid chatId, CancellationToken cancellationToken = default);
    Task<bool> MessageExistsAsync(Guid clientMessageId, CancellationToken cancellationToken = default);

    Task UpdatePlaintextBodyAsync(Guid messageId, string plaintextBody, CancellationToken ct = default);

    Task<string?> GetPlaintextBodyAsync(Guid messageId, CancellationToken ct = default);

    Task UpsertChatAsync(ChatSummaryDto chat, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ChatSummaryDto>> GetChatsAsync(CancellationToken cancellationToken = default);

    Task ResetUnreadCountAsync(Guid chatId, CancellationToken cancellationToken = default);

    Task UpdateMessageStatusAsync(Guid messageId, MessageStatus status, CancellationToken cancellationToken = default);

    Task MarkMessagesReadAsync(Guid chatId, Guid currentUserId, CancellationToken cancellationToken = default);

    Task DeleteChatAsync(Guid chatId, CancellationToken ct = default);

    Task ClearMessagesAsync(Guid chatId, CancellationToken ct = default);
}
