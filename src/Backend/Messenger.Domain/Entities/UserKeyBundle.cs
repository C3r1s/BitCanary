using Messenger.Domain.Abstractions;

namespace Messenger.Domain.Entities;

public sealed class UserKeyBundle : Entity
{
    public Guid UserId { get; set; }
    public Guid DeviceId { get; set; }
    public byte[] IkPublic { get; set; } = Array.Empty<byte>();
    public byte[] SpkPublic { get; set; } = Array.Empty<byte>();
    public byte[] SpkSignature { get; set; } = Array.Empty<byte>();
    public DateTimeOffset SpkCreatedAt { get; set; }
}
