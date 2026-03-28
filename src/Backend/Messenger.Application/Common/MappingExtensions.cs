using Messenger.Domain.Entities;
using Messenger.Shared.Contracts.Dtos;

namespace Messenger.Application.Common;

public static class MappingExtensions
{
    public static UserProfileDto ToDto(this User user) =>
        new(
            user.Id,
            user.UserName,
            user.DisplayName,
            user.Bio,
            user.AvatarUrl,
            user.LastSeenUtc,
            user.PublicKey);

    public static UserSettingsDto ToDto(this UserSettings settings) =>
        new(settings.ThemePreference, settings.SendByEnter, settings.UseCompactMode, settings.EnableCustomEmoji);

    public static ChatMemberDto ToDto(this ChatMembership membership) =>
        new(
            membership.UserId,
            membership.User.DisplayName,
            membership.User.AvatarUrl,
            membership.Role,
            membership.JoinedAtUtc);

    public static MessageDto ToDto(this Message message) =>
        new(
            message.Id,
            message.ChatId,
            message.SenderId,
            message.Sender.DisplayName,
            message.Kind,
            message.EncryptedPayload,
            message.EncryptionAlgorithm,
            message.KeyEnvelope,
            message.MediaId,
            message.ReplyToMessageId,
            message.MetadataJson,
            message.CreatedAtUtc);

    public static ChatSummaryDto ToDto(this Chat chat, MessageDto? lastMessage, int unreadCount) =>
        new(
            chat.Id,
            chat.Title,
            chat.Type,
            chat.AvatarUrl,
            chat.Description,
            lastMessage,
            unreadCount,
            chat.Memberships.Select(static x => x.ToDto()).ToArray());

    public static FolderDto ToDto(this Folder folder) =>
        new(
            folder.Id,
            folder.Name,
            folder.Order,
            folder.ChatLinks.Select(static x => x.ChatId).ToArray());
}
