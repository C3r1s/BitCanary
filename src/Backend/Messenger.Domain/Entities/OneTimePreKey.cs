// Доменная сущность «OneTimePreKey»: модель данных для персистентности BitCanary.
using Messenger.Domain.Abstractions;

namespace Messenger.Domain.Entities;

public sealed class OneTimePreKey : Entity
{
    public Guid UserId { get; set; }
    public byte[] PublicKey { get; set; } = Array.Empty<byte>();
    public DateTimeOffset? ClaimedAt { get; set; }
}
