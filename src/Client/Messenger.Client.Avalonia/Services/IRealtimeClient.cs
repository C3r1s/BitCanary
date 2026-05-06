// Сервис клиента BitCanary: сеть, кэш, медиа — «IRealtimeClient».
using Messenger.Shared.Contracts;
using Messenger.Shared.Contracts.Dtos;
using Messenger.Shared.Contracts.Realtime;

namespace Messenger.Client.Avalonia.Services;

public interface IRealtimeClient : IAsyncDisposable
{
    event Func<MessageDto, Task>? MessageReceived;
    event Func<TypingIndicatorDto, Task>? TypingReceived;
    event Func<CallSignalDto, Task>? CallSignalReceived;
    event Func<PresenceChangedDto, Task>? PresenceChanged;

    event Func<Task>? OtpkSupplyLow;

    event Func<Guid, Task>? MessageDelivered;

    event Func<Guid, Guid, Task>? MessagesRead;

    event Func<Guid, Task>? RemovedFromChat;

    event Action<ConnectionState>? ConnectionStateChanged;

    event Action? ReconnectedAndNeedsRefresh;

    Task ConnectAsync(CancellationToken cancellationToken = default);
    Task JoinChatAsync(Guid chatId, CancellationToken cancellationToken = default);

    Task LeaveChatAsync(Guid chatId, CancellationToken cancellationToken = default);

    Task SendTypingIndicatorAsync(Guid chatId, bool isTyping, CancellationToken cancellationToken = default);

    Task SendReadReceiptAsync(Guid chatId, CancellationToken cancellationToken = default);

    void SetCurrentlySelectedChatId(Guid? chatId);
}
