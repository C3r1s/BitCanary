// SignalR-хаб: сообщения в реальном времени, индикаторы набора, чтение и сигналы звонков.
using System.Security.Claims;
using Messenger.Application.Abstractions;
using Messenger.Application.Messages;
using Messenger.Api.Services;
using Messenger.Shared.Contracts;
using Messenger.Shared.Contracts.Dtos;
using Messenger.Shared.Contracts.Realtime;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Messenger.Api.Hubs;

[Authorize]
public sealed class ChatHub(
    ConnectionMappingService connectionMappingService,
    IAppDbContext dbContext,
    IMessageService messageService,
    ICallService callService,
    IRealtimeNotifier realtimeNotifier) : Hub
{
    public override async Task OnConnectedAsync()
    {
        var userId = GetRequiredUserId();
        connectionMappingService.Add(userId, Context.ConnectionId);

        await realtimeNotifier.BroadcastPresenceAsync(new PresenceChangedDto(userId, null, true), Context.ConnectionAborted);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var userId = GetRequiredUserId();
        connectionMappingService.Remove(userId, Context.ConnectionId);

        var user = await dbContext.Users.SingleOrDefaultAsync(x => x.Id == userId, Context.ConnectionAborted);
        if (user is not null)
        {
            user.LastSeenUtc = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync(Context.ConnectionAborted);
        }

        await realtimeNotifier.BroadcastPresenceAsync(
            new PresenceChangedDto(userId, DateTimeOffset.UtcNow, false),
            CancellationToken.None);

        await base.OnDisconnectedAsync(exception);
    }

    public async Task JoinChat(Guid chatId)
    {
        var userId = GetRequiredUserId();
        var isMember = await dbContext.ChatMemberships
            .AnyAsync(x => x.ChatId == chatId && x.UserId == userId, Context.ConnectionAborted);

        if (!isMember)
        {
            throw new HubException("User is not a member of the chat.");
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, chatId.ToString());
    }

    public Task LeaveChat(Guid chatId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, chatId.ToString());

    public async Task<MessageDto> SendMessage(SendMessageRequest request)
    {
        var command = new SendMessageCommand(
            request.ChatId,
            request.ClientMessageId,
            request.Kind,
            request.EncryptedPayload,
            request.EncryptionAlgorithm,
            request.KeyEnvelope,
            request.MediaId,
            request.ReplyToMessageId,
            request.MetadataJson);

        var message = await messageService.SendAsync(command, Context.ConnectionAborted);
        return message;
    }

    public async Task ReadMessages(Guid chatId)
    {
        var userId = GetRequiredUserId();
        var membership = await dbContext.ChatMemberships
            .SingleOrDefaultAsync(x => x.ChatId == chatId && x.UserId == userId, Context.ConnectionAborted);
        if (membership is null)
        {
            throw new HubException("User is not a member of the chat.");
        }

        membership.LastReadAtUtc = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(Context.ConnectionAborted);
        await realtimeNotifier.SendMessagesReadAsync(chatId, userId, Context.ConnectionAborted);
    }

    public async Task TypingIndicator(Guid chatId, bool isTyping)
    {
        var userId = GetRequiredUserId();
        var displayName = await dbContext.Users
            .Where(x => x.Id == userId)
            .Select(x => x.DisplayName)
            .SingleAsync(Context.ConnectionAborted);

        var payload = new TypingIndicatorDto(chatId, userId, displayName, isTyping);
        await realtimeNotifier.BroadcastTypingAsync(payload, Context.ConnectionAborted);
    }

    public Task SendOffer(Guid chatId, Guid toUserId, string payload) =>
        SendCallSignal(chatId, toUserId, CallSignalKind.Offer, payload);

    public Task SendAnswer(Guid chatId, Guid toUserId, string payload) =>
        SendCallSignal(chatId, toUserId, CallSignalKind.Answer, payload);

    public Task SendIceCandidate(Guid chatId, Guid toUserId, string payload) =>
        SendCallSignal(chatId, toUserId, CallSignalKind.IceCandidate, payload);

    private Task SendCallSignal(Guid chatId, Guid toUserId, CallSignalKind kind, string payload)
    {
        var signal = new CallSignalDto(chatId, GetRequiredUserId(), toUserId, kind, payload, DateTimeOffset.UtcNow);
        return callService.RelaySignalAsync(signal, Context.ConnectionAborted);
    }

    private Guid GetRequiredUserId()
    {
        var value = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(value, out var userId))
        {
            throw new HubException("Unauthorized connection context.");
        }

        return userId;
    }
}
