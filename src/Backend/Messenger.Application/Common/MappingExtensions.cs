// Преобразование сущностей EF в DTO для контрактов клиента.
using Messenger.Domain.Entities;
using Messenger.Shared.Contracts;
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
        new(
            settings.ThemePreference,
            settings.SendByEnter,
            settings.UseCompactMode,
            settings.EnableCustomEmoji,
            settings.ShowNotifications,
            settings.ShowSenderName,
            settings.TerminalColorScheme);

    public static ChatMemberDto ToDto(this ChatMembership membership) =>
        new(
            membership.UserId,
            membership.User.DisplayName,
            membership.User.AvatarUrl,
            membership.Role,
            membership.JoinedAtUtc);

    public static MessageDto ToDto(this Message message, MessageStatus status = MessageStatus.Delivered) =>
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
            message.CreatedAtUtc,
            message.ProtocolVersion,
            status);

    public static ChatSummaryDto ToDto(this Chat chat, Guid viewerUserId, MessageDto? lastMessage, int unreadCount)
    {
        var title = chat.Title;
        if (chat.Type == ChatType.Direct)
        {
            var peerMembership = chat.Memberships.FirstOrDefault(m => m.UserId != viewerUserId);
            var peerUser = peerMembership?.User;
            if (peerUser is not null && !string.IsNullOrWhiteSpace(peerUser.DisplayName))
            {
                title = peerUser.DisplayName;
            }
        }

        return new ChatSummaryDto(
            chat.Id,
            title,
            chat.Type,
            chat.AvatarUrl,
            chat.Description,
            lastMessage,
            unreadCount,
            chat.Memberships.Select(static x => x.ToDto()).ToArray());
    }

    public static FolderDto ToDto(this Folder folder) =>
        new(
            folder.Id,
            folder.Name,
            folder.Order,
            folder.ChatLinks.Select(static x => x.ChatId).ToArray());
}
