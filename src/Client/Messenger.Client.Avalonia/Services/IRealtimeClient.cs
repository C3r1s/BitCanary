using Messenger.Shared.Contracts.Dtos;
using Messenger.Shared.Contracts.Realtime;

namespace Messenger.Client.Avalonia.Services;

public interface IRealtimeClient : IAsyncDisposable
{
    event Func<MessageDto, Task>? MessageReceived;
    event Func<TypingIndicatorDto, Task>? TypingReceived;
    event Func<CallSignalDto, Task>? CallSignalReceived;
    event Func<PresenceChangedDto, Task>? PresenceChanged;
    Task ConnectAsync(CancellationToken cancellationToken = default);
    Task JoinChatAsync(Guid chatId, CancellationToken cancellationToken = default);
    Task SendTypingIndicatorAsync(Guid chatId, bool isTyping, CancellationToken cancellationToken = default);
}
