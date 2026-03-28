using System.Net;
using Messenger.Application.Abstractions;
using Messenger.Application.Common;
using Messenger.Shared.Contracts.Dtos;
using Microsoft.EntityFrameworkCore;

namespace Messenger.Application.Messages;

public sealed class MessageService(
    IAppDbContext dbContext,
    ICurrentUserContext currentUser,
    SendMessageCommandHandler sendMessageCommandHandler) : IMessageService
{
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

        return messages.Select(static x => x.ToDto()).ToArray();
    }

    public Task<MessageDto> SendAsync(SendMessageCommand command, CancellationToken cancellationToken) =>
        sendMessageCommandHandler.HandleAsync(command, cancellationToken);
}
