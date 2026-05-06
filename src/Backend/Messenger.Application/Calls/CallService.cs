// Маршрутизация WebRTC-сигналов между участниками через SignalR.
using Messenger.Application.Abstractions;
using Messenger.Application.Common;
using Messenger.Domain.Entities;
using Messenger.Shared.Contracts.Dtos;
using Microsoft.EntityFrameworkCore;

namespace Messenger.Application.Calls;

public sealed class CallService(
    IAppDbContext dbContext,
    ICurrentUserContext currentUser,
    IRealtimeNotifier realtimeNotifier) : ICallService
{
    public async Task RelaySignalAsync(CallSignalDto signal, CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireUserId();

        var canAccessChat = await dbContext.ChatMemberships
            .AnyAsync(x => x.ChatId == signal.ChatId && x.UserId == userId, cancellationToken);

        if (!canAccessChat)
        {
            throw new AppException("User is not a member of the chat.");
        }

        var logEntry = new CallSignalLog
        {
            ChatId = signal.ChatId,
            FromUserId = userId,
            ToUserId = signal.ToUserId,
            Kind = signal.Kind,
            Payload = signal.Payload
        };

        dbContext.CallSignalLogs.Add(logEntry);
        await dbContext.SaveChangesAsync(cancellationToken);

        var outboundSignal = signal with
        {
            FromUserId = userId,
            SentAtUtc = logEntry.CreatedAtUtc
        };

        await realtimeNotifier.SendCallSignalAsync(outboundSignal, cancellationToken);
    }
}
