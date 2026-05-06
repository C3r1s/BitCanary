// Доменная сущность «CallSignalLog»: модель данных для персистентности BitCanary.
using Messenger.Domain.Abstractions;
using Messenger.Shared.Contracts;

namespace Messenger.Domain.Entities;

public sealed class CallSignalLog : Entity
{
    public Guid ChatId { get; set; }
    public Guid FromUserId { get; set; }
    public Guid ToUserId { get; set; }
    public CallSignalKind Kind { get; set; }
    public string Payload { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
