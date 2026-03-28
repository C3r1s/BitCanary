using Messenger.Shared.Contracts;

namespace Messenger.Application.Messages;

public sealed record SendMessageCommand(
    Guid ChatId,
    Guid ClientMessageId,
    MessageKind Kind,
    string EncryptedPayload,
    string EncryptionAlgorithm,
    string KeyEnvelope,
    Guid? MediaId,
    Guid? ReplyToMessageId,
    string? MetadataJson,
    ProtocolVersion ProtocolVersion = ProtocolVersion.LegacyAes);
