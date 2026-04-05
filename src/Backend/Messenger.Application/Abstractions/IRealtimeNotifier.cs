using Messenger.Shared.Contracts.Dtos;
using Messenger.Shared.Contracts.Realtime;

namespace Messenger.Application.Abstractions;

public interface IRealtimeNotifier
{
    Task BroadcastMessageAsync(MessageDto message, CancellationToken cancellationToken);
    Task BroadcastTypingAsync(TypingIndicatorDto typingIndicator, CancellationToken cancellationToken);
    Task SendCallSignalAsync(CallSignalDto signal, CancellationToken cancellationToken);
    Task BroadcastPresenceAsync(PresenceChangedDto presenceChanged, CancellationToken cancellationToken);
    Task SendOtpkSupplyLowAsync(Guid userId, CancellationToken cancellationToken);
    Task SendMessageDeliveredAsync(Guid messageId, Guid senderId, CancellationToken cancellationToken);
    Task SendMessagesReadAsync(Guid chatId, Guid readByUserId, CancellationToken cancellationToken);
}
