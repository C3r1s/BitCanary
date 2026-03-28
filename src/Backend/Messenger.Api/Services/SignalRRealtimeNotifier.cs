using Messenger.Application.Abstractions;
using Messenger.Api.Hubs;
using Messenger.Shared.Contracts.Dtos;
using Messenger.Shared.Contracts.Realtime;
using Microsoft.AspNetCore.SignalR;

namespace Messenger.Api.Services;

public sealed class SignalRRealtimeNotifier(
    IHubContext<ChatHub> hubContext,
    ConnectionMappingService connectionMappingService) : IRealtimeNotifier
{
    public Task BroadcastMessageAsync(MessageDto message, CancellationToken cancellationToken) =>
        hubContext.Clients.Group(message.ChatId.ToString())
            .SendAsync(RealtimeEventNames.MessageReceived, message, cancellationToken);

    public Task BroadcastTypingAsync(TypingIndicatorDto typingIndicator, CancellationToken cancellationToken) =>
        hubContext.Clients.Group(typingIndicator.ChatId.ToString())
            .SendAsync(RealtimeEventNames.TypingIndicator, typingIndicator, cancellationToken);

    public async Task SendCallSignalAsync(CallSignalDto signal, CancellationToken cancellationToken)
    {
        var recipientConnections = connectionMappingService.GetConnections(signal.ToUserId);
        if (recipientConnections.Count == 0)
        {
            return;
        }

        await hubContext.Clients.Clients(recipientConnections)
            .SendAsync(RealtimeEventNames.CallSignalReceived, signal, cancellationToken);
    }

    public Task BroadcastPresenceAsync(PresenceChangedDto presenceChanged, CancellationToken cancellationToken) =>
        hubContext.Clients.All.SendAsync(RealtimeEventNames.PresenceChanged, presenceChanged, cancellationToken);
}
