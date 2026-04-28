using System.Net;
using Messenger.Application.Abstractions;
using Messenger.Application.Common;
using Messenger.Domain.Entities;
using Messenger.Shared.Contracts.Dtos;
using Microsoft.EntityFrameworkCore;

namespace Messenger.Application.Messages;

public sealed class SendMessageCommandHandler(
    IAppDbContext dbContext,
    ICurrentUserContext currentUser,
    IRealtimeNotifier realtimeNotifier)
{
    public async Task<MessageDto> HandleAsync(SendMessageCommand command, CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireUserId();

        var membership = await dbContext.ChatMemberships
            .SingleOrDefaultAsync(
                x => x.ChatId == command.ChatId && x.UserId == userId,
                cancellationToken);

        if (membership is null)
        {
            throw new AppException("User is not a member of the chat.", HttpStatusCode.Forbidden);
        }

        var existingMessage = await dbContext.Messages
            .Include(x => x.Sender)
            .SingleOrDefaultAsync(
                x => x.ChatId == command.ChatId &&
                     x.SenderId == userId &&
                     x.ClientMessageId == command.ClientMessageId,
                cancellationToken);

        if (existingMessage is not null)
        {
            return existingMessage.ToDto();
        }

        if (command.MediaId.HasValue)
        {
            var mediaExists = await dbContext.MediaAssets
                .AnyAsync(x => x.Id == command.MediaId.Value, cancellationToken);

            if (!mediaExists)
            {
                throw new AppException("The referenced media file does not exist.");
            }
        }

        if (command.ReplyToMessageId.HasValue)
        {
            var replyExists = await dbContext.Messages
                .AnyAsync(x => x.Id == command.ReplyToMessageId.Value && x.ChatId == command.ChatId, cancellationToken);

            if (!replyExists)
            {
                throw new AppException("The reply target does not exist in this chat.");
            }
        }

        var sender = await dbContext.Users.SingleAsync(x => x.Id == userId, cancellationToken);

        var message = new Message
        {
            ChatId = command.ChatId,
            SenderId = userId,
            Sender = sender,
            ClientMessageId = command.ClientMessageId,
            Kind = command.Kind,
            EncryptedPayload = command.EncryptedPayload,
            EncryptionAlgorithm = command.EncryptionAlgorithm,
            KeyEnvelope = command.KeyEnvelope,
            MediaId = command.MediaId,
            ReplyToMessageId = command.ReplyToMessageId,
            MetadataJson = command.MetadataJson,
            ProtocolVersion = command.ProtocolVersion
        };

        dbContext.Messages.Add(message);
        membership.LastReadAtUtc = message.CreatedAtUtc;

        await dbContext.SaveChangesAsync(cancellationToken);

        var savedMessage = await dbContext.Messages
            .Include(m => m.Sender)
            .SingleAsync(x => x.Id == message.Id, cancellationToken);

        var dto = savedMessage.ToDto();
        await realtimeNotifier.BroadcastMessageAsync(dto, cancellationToken);

        return dto;
    }
}
