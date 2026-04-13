using System.Net;
using Messenger.Application.Abstractions;
using Messenger.Application.Common;
using Messenger.Domain.Entities;
using Messenger.Shared.Contracts;
using Messenger.Shared.Contracts.Dtos;
using Microsoft.EntityFrameworkCore;

namespace Messenger.Application.Chats;

public sealed class ChatService(IAppDbContext dbContext, ICurrentUserContext currentUser) : IChatService
{
    public async Task<IReadOnlyCollection<ChatSummaryDto>> GetChatsAsync(CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireUserId();

        var chats = await dbContext.Chats
            .AsNoTracking()
            .Where(x => x.Memberships.Any(m => m.UserId == userId))
            .Include(x => x.Memberships)
            .ThenInclude(x => x.User)
            .ToListAsync(cancellationToken);

        var chatIds = chats.Select(static x => x.Id).ToArray();

        var lastMessages = await dbContext.Messages
            .AsNoTracking()
            .Where(x => chatIds.Contains(x.ChatId))
            .Include(x => x.Sender)
            .GroupBy(x => x.ChatId)
            .Select(x => x.OrderByDescending(m => m.CreatedAtUtc).First())
            .ToListAsync(cancellationToken);

        var lastMessageLookup = lastMessages.ToDictionary(x => x.ChatId, static x => x.ToDto());
        var membershipLookup = chats
            .Select(x => x.Memberships.Single(m => m.UserId == userId))
            .ToDictionary(x => x.ChatId);

        var unreadCounts = await dbContext.Messages
            .AsNoTracking()
            .Where(x => chatIds.Contains(x.ChatId) && x.SenderId != userId)
            .GroupJoin(
                dbContext.ChatMemberships.Where(x => x.UserId == userId),
                message => message.ChatId,
                membership => membership.ChatId,
                (message, memberships) => new
                {
                    message.ChatId,
                    message.CreatedAtUtc,
                    Membership = memberships.Single()
                })
            .Where(x => x.CreatedAtUtc > (x.Membership.LastReadAtUtc ?? DateTimeOffset.MinValue))
            .GroupBy(x => x.ChatId)
            .Select(x => new { ChatId = x.Key, Count = x.Count() })
            .ToDictionaryAsync(x => x.ChatId, x => x.Count, cancellationToken);

        return chats
            .Select(chat => chat.ToDto(
                lastMessageLookup.GetValueOrDefault(chat.Id),
                unreadCounts.GetValueOrDefault(chat.Id)))
            .OrderByDescending(x => x.LastMessage?.CreatedAtUtc ?? DateTimeOffset.MinValue)
            .ToArray();
    }

    public async Task<ChatSummaryDto> CreateChatAsync(CreateChatRequest request, CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireUserId();

        var memberIds = request.MemberIds
            .Append(userId)
            .Distinct()
            .ToArray();

        if (request.Type == ChatType.Direct && memberIds.Length != 2)
        {
            throw new AppException("Direct chats must contain exactly two members.");
        }

        var members = await dbContext.Users
            .Where(x => memberIds.Contains(x.Id))
            .ToListAsync(cancellationToken);

        if (members.Count != memberIds.Length)
        {
            throw new AppException("One or more chat members were not found.");
        }

        var chat = new Chat
        {
            Title = string.IsNullOrWhiteSpace(request.Title)
                ? request.Type == ChatType.Direct
                    ? members.Single(x => x.Id != userId).DisplayName
                    : "New Chat"
                : request.Title.Trim(),
            Type = request.Type,
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim()
        };

        foreach (var memberId in memberIds)
        {
            chat.Memberships.Add(new ChatMembership
            {
                Chat = chat,
                UserId = memberId,
                Role = memberId == userId ? ChatRole.Owner : ChatRole.Member
            });
        }

        dbContext.Chats.Add(chat);
        await dbContext.SaveChangesAsync(cancellationToken);

        var createdChat = await dbContext.Chats
            .AsNoTracking()
            .Include(x => x.Memberships)
            .ThenInclude(x => x.User)
            .SingleAsync(x => x.Id == chat.Id, cancellationToken);

        return createdChat.ToDto(null, 0);
    }

    public async Task<IReadOnlyCollection<FolderDto>> GetFoldersAsync(CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireUserId();

        var folders = await dbContext.Folders
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .Include(x => x.ChatLinks)
            .OrderBy(x => x.Order)
            .ToListAsync(cancellationToken);

        return folders.Select(static x => x.ToDto()).ToArray();
    }

    public async Task<FolderDto> CreateFolderAsync(CreateFolderRequest request, CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireUserId();

        var allowedChatIds = await dbContext.ChatMemberships
            .Where(x => x.UserId == userId && request.ChatIds.Contains(x.ChatId))
            .Select(x => x.ChatId)
            .ToListAsync(cancellationToken);

        if (allowedChatIds.Count != request.ChatIds.Count)
        {
            throw new AppException("Folders can only contain chats the current user belongs to.", HttpStatusCode.Forbidden);
        }

        var folder = new Folder
        {
            UserId = userId,
            Name = request.Name.Trim(),
            Order = request.Order
        };

        foreach (var chatId in request.ChatIds.Distinct())
        {
            folder.ChatLinks.Add(new UserFolderChat
            {
                Folder = folder,
                ChatId = chatId
            });
        }

        dbContext.Folders.Add(folder);
        await dbContext.SaveChangesAsync(cancellationToken);

        return folder.ToDto();
    }

    public async Task<ChatSummaryDto> AddMemberAsync(Guid chatId, Guid userId, CancellationToken cancellationToken)
    {
        var callerId = currentUser.RequireUserId();

        var callerMembership = await dbContext.ChatMemberships
            .SingleOrDefaultAsync(x => x.ChatId == chatId && x.UserId == callerId, cancellationToken);

        if (callerMembership is null)
            throw new AppException("User is not a member of the chat.", HttpStatusCode.Forbidden);

        if (callerMembership.Role > ChatRole.Admin)
            throw new AppException("Only Admins and Owners can add members.", HttpStatusCode.Forbidden);

        var alreadyMember = await dbContext.ChatMemberships
            .AnyAsync(x => x.ChatId == chatId && x.UserId == userId, cancellationToken);

        if (alreadyMember)
            throw new AppException("User is already a member of the chat.", HttpStatusCode.Conflict);

        dbContext.ChatMemberships.Add(new ChatMembership
        {
            ChatId = chatId,
            UserId = userId,
            Role = ChatRole.Member
        });

        await dbContext.SaveChangesAsync(cancellationToken);

        var updated = await dbContext.Chats.AsNoTracking()
            .Include(x => x.Memberships).ThenInclude(x => x.User)
            .SingleAsync(x => x.Id == chatId, cancellationToken);

        return updated.ToDto(null, 0);
    }

    public async Task RemoveMemberAsync(Guid chatId, Guid userId, CancellationToken cancellationToken)
    {
        var callerId = currentUser.RequireUserId();

        var callerMembership = await dbContext.ChatMemberships
            .SingleOrDefaultAsync(x => x.ChatId == chatId && x.UserId == callerId, cancellationToken);

        if (callerMembership is null)
            throw new AppException("User is not a member of the chat.", HttpStatusCode.Forbidden);

        if (userId == callerId)
        {
            // Self-remove (leave)
            if (callerMembership.Role == ChatRole.Owner)
                throw new AppException("Owner must transfer ownership or delete the group before leaving.", HttpStatusCode.BadRequest);

            dbContext.ChatMemberships.Remove(callerMembership);
            await dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        // Removing another user
        if (callerMembership.Role > ChatRole.Admin)
            throw new AppException("Only Admins and Owners can remove members.", HttpStatusCode.Forbidden);

        var targetMembership = await dbContext.ChatMemberships
            .SingleOrDefaultAsync(x => x.ChatId == chatId && x.UserId == userId, cancellationToken);

        if (targetMembership is null)
            throw new AppException("Target user is not a member of the chat.", HttpStatusCode.NotFound);

        if (targetMembership.Role == ChatRole.Owner)
            throw new AppException("Cannot remove the group Owner.", HttpStatusCode.Forbidden);

        if (callerMembership.Role == ChatRole.Admin && targetMembership.Role <= ChatRole.Admin)
            throw new AppException("Admins can only remove Members.", HttpStatusCode.Forbidden);

        dbContext.ChatMemberships.Remove(targetMembership);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateMemberRoleAsync(Guid chatId, Guid userId, ChatRole role, CancellationToken cancellationToken)
    {
        var callerId = currentUser.RequireUserId();

        var callerMembership = await dbContext.ChatMemberships
            .SingleOrDefaultAsync(x => x.ChatId == chatId && x.UserId == callerId, cancellationToken);

        if (callerMembership is null)
            throw new AppException("User is not a member of the chat.", HttpStatusCode.Forbidden);

        if (callerMembership.Role != ChatRole.Owner)
            throw new AppException("Only the Owner can change member roles.", HttpStatusCode.Forbidden);

        if (userId == callerId)
            throw new AppException("Owner cannot change their own role.", HttpStatusCode.BadRequest);

        if (role == ChatRole.Owner)
            throw new AppException("Cannot assign Owner role. Use a dedicated ownership transfer operation.", HttpStatusCode.BadRequest);

        var targetMembership = await dbContext.ChatMemberships
            .SingleOrDefaultAsync(x => x.ChatId == chatId && x.UserId == userId, cancellationToken);

        if (targetMembership is null)
            throw new AppException("Target user is not a member of the chat.", HttpStatusCode.NotFound);

        targetMembership.Role = role;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<ChatSummaryDto> UpdateChatAsync(Guid chatId, UpdateChatRequest request, CancellationToken cancellationToken)
    {
        var callerId = currentUser.RequireUserId();

        var callerMembership = await dbContext.ChatMemberships
            .SingleOrDefaultAsync(x => x.ChatId == chatId && x.UserId == callerId, cancellationToken);

        if (callerMembership is null)
            throw new AppException("User is not a member of the chat.", HttpStatusCode.Forbidden);

        if (callerMembership.Role > ChatRole.Admin)
            throw new AppException("Only Admins and Owners can update chat metadata.", HttpStatusCode.Forbidden);

        var chat = await dbContext.Chats
            .SingleOrDefaultAsync(x => x.Id == chatId, cancellationToken);

        if (chat is null)
            throw new AppException("Chat not found.", HttpStatusCode.NotFound);

        if (!string.IsNullOrWhiteSpace(request.Title))
            chat.Title = request.Title.Trim();

        if (request.Description is not null)
            chat.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();

        await dbContext.SaveChangesAsync(cancellationToken);

        var updated = await dbContext.Chats.AsNoTracking()
            .Include(x => x.Memberships).ThenInclude(x => x.User)
            .SingleAsync(x => x.Id == chatId, cancellationToken);

        return updated.ToDto(null, 0);
    }
}
