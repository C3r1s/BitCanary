using Messenger.Shared.Contracts;

namespace Messenger.Domain.Entities;

public sealed class ChatMembership
{
    public Guid ChatId { get; set; }
    public Chat Chat { get; set; } = null!;
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public ChatRole Role { get; set; } = ChatRole.Member;
    public DateTimeOffset JoinedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastReadAtUtc { get; set; }
    public bool IsMuted { get; set; }
}
