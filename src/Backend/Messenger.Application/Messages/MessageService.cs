// Выдача истории сообщений, очистка переписки и статусы доставки для участника.
using System.Net;
using Messenger.Application.Abstractions;
using Messenger.Application.Common;
using Messenger.Shared.Contracts;
using Messenger.Shared.Contracts.Dtos;
using Microsoft.EntityFrameworkCore;

namespace Messenger.Application.Messages;

public sealed class MessageService(
    IAppDbContext dbContext,
    ICurrentUserContext currentUser,
    SendMessageCommandHandler sendMessageCommandHandler) : IMessageService
{
    public async Task ClearChatMessagesAsync(Guid chatId, CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireUserId();
        var membership = await dbContext.ChatMemberships
            .SingleOrDefaultAsync(x => x.ChatId == chatId && x.UserId == userId, cancellationToken);
        if (membership is null)
        {
            throw new AppException("User is not a member of the chat.", HttpStatusCode.Forbidden);
        }
        if (membership.Role > ChatRole.Admin)
        {
            throw new AppException("Only Admins and Owners can clear chat messages.", HttpStatusCode.Forbidden);
        }

        await dbContext.Messages
            .Where(x => x.ChatId == chatId)
            .ExecuteDeleteAsync(cancellationToken);
    }

    public async Task<IReadOnlyCollection<MessageDto>> GetMessagesAsync(Guid chatId, CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireUserId();

        var isMember = await dbContext.ChatMemberships
            .AnyAsync(x => x.ChatId == chatId && x.UserId == userId, cancellationToken);

        if (!isMember)
        {
            throw new AppException("User is not a member of the chat.", HttpStatusCode.Forbidden);
        }

        var messages = await dbContext.Messages
            .AsNoTracking()
            .Include(x => x.Sender)
            .Where(x => x.ChatId == chatId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(100)
            .OrderBy(x => x.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        var memberships = await dbContext.ChatMemberships
            .AsNoTracking()
            .Where(x => x.ChatId == chatId)
            .Select(x => new { x.UserId, x.LastReadAtUtc })
            .ToListAsync(cancellationToken);

        var otherMemberReads = memberships
            .Where(x => x.UserId != userId)
            .Select(x => x.LastReadAtUtc)
            .ToArray();

        return messages
            .Select(message =>
            {
                var status = MessageStatus.Delivered;
                if (message.SenderId == userId && otherMemberReads.Length > 0)
                {
                    var seenByAnyPeer = otherMemberReads.Any(lastRead =>
                        lastRead.HasValue && lastRead.Value >= message.CreatedAtUtc);
                    status = seenByAnyPeer ? MessageStatus.Read : MessageStatus.Delivered;
                }
                return message.ToDto(status);
            })
            .ToArray();
    }

    public Task<MessageDto> SendAsync(SendMessageCommand command, CancellationToken cancellationToken) =>
        sendMessageCommandHandler.HandleAsync(command, cancellationToken);
}
