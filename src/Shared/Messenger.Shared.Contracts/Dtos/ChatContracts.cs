namespace Messenger.Shared.Contracts.Dtos;

public sealed record ChatMemberDto(
    Guid UserId,
    string DisplayName,
    string? AvatarUrl,
    ChatRole Role,
    DateTimeOffset JoinedAtUtc);

public sealed record FolderDto(
    Guid Id,
    string Name,
    int Order,
    IReadOnlyCollection<Guid> ChatIds);

public sealed record CreateFolderRequest(
    string Name,
    int Order,
    IReadOnlyCollection<Guid> ChatIds);

public sealed record CreateChatRequest(
    string Title,
    ChatType Type,
    string? Description,
    IReadOnlyCollection<Guid> MemberIds);

public sealed record ChatSummaryDto(
    Guid Id,
    string Title,
    ChatType Type,
    string? AvatarUrl,
    string? Description,
    MessageDto? LastMessage,
    int UnreadCount,
    IReadOnlyCollection<ChatMemberDto> Members);
