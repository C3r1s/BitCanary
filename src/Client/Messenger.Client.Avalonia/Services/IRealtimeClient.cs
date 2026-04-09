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

    /// <summary>Raised when the server reports the user's OPK pool is running low.</summary>
    event Func<Task>? OtpkSupplyLow;

    /// <summary>Raised when a message sent by the current user has been delivered (server ACK).</summary>
    event Func<Guid, Task>? MessageDelivered;

    /// <summary>Raised when another user has read messages in a chat.</summary>
    event Func<Guid, Guid, Task>? MessagesRead;

    /// <summary>
    /// Raised when the SignalR connection state changes. Fired from hub Reconnecting /
    /// Reconnected / Closed callbacks and the background retry loop.
    /// </summary>
    event Action<ConnectionState>? ConnectionStateChanged;

    /// <summary>Raised after a successful reconnect so MainWindowViewModel can refresh remote data.</summary>
    event Action? ReconnectedAndNeedsRefresh;

    Task ConnectAsync(CancellationToken cancellationToken = default);
    Task JoinChatAsync(Guid chatId, CancellationToken cancellationToken = default);
    Task SendTypingIndicatorAsync(Guid chatId, bool isTyping, CancellationToken cancellationToken = default);

    /// <summary>Sends a ReadMessages hub method to notify senders their messages have been read.</summary>
    Task SendReadReceiptAsync(Guid chatId, CancellationToken cancellationToken = default);

    /// <summary>Tracks the currently selected chat so it can be re-joined on reconnect.</summary>
    void SetCurrentlySelectedChatId(Guid? chatId);
}
