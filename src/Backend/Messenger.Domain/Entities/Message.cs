// Доменная сущность «Message»: модель данных для персистентности BitCanary.
using Messenger.Domain.Abstractions;
using Messenger.Shared.Contracts;

namespace Messenger.Domain.Entities;

public sealed class Message : Entity
{
    public Guid ChatId { get; set; }
    public Chat Chat { get; set; } = null!;
    public Guid SenderId { get; set; }
    public User Sender { get; set; } = null!;
    public Guid ClientMessageId { get; set; }
    public MessageKind Kind { get; set; } = MessageKind.Text;
    public string EncryptedPayload { get; set; } = string.Empty;
    public string EncryptionAlgorithm { get; set; } = string.Empty;
    public string KeyEnvelope { get; set; } = string.Empty;
    public Guid? MediaId { get; set; }
    public MediaAsset? Media { get; set; }
    public Guid? ReplyToMessageId { get; set; }
    public Message? ReplyToMessage { get; set; }
    public ICollection<Message> Replies { get; set; } = new List<Message>();
    public string? MetadataJson { get; set; }
    public ProtocolVersion ProtocolVersion { get; set; } = ProtocolVersion.LegacyAes;
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
