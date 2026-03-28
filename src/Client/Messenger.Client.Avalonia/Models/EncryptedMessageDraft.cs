using Messenger.Shared.Contracts;

namespace Messenger.Client.Avalonia.Models;

public sealed record EncryptedMessageDraft(
    MessageKind Kind,
    string EncryptedPayload,
    string EncryptionAlgorithm,
    string KeyEnvelope,
    string? MetadataJson);
