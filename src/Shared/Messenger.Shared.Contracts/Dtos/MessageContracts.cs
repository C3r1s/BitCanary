namespace Messenger.Shared.Contracts.Dtos;

public sealed record SendMessageRequest(
    Guid ChatId,
    Guid ClientMessageId,
    MessageKind Kind,
    string EncryptedPayload,
    string EncryptionAlgorithm,
    string KeyEnvelope,
    Guid? MediaId,
    Guid? ReplyToMessageId,
    string? MetadataJson);

public sealed record MessageDto(
    Guid Id,
    Guid ChatId,
    Guid SenderId,
    string SenderDisplayName,
    MessageKind Kind,
    string EncryptedPayload,
    string EncryptionAlgorithm,
    string KeyEnvelope,
    Guid? MediaId,
    Guid? ReplyToMessageId,
    string? MetadataJson,
    DateTimeOffset CreatedAtUtc);

public sealed record TypingIndicatorDto(
    Guid ChatId,
    Guid UserId,
    string DisplayName,
    bool IsTyping);
