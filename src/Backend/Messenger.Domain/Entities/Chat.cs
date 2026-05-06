// Доменная сущность «Chat»: модель данных для персистентности BitCanary.
using Messenger.Domain.Abstractions;
using Messenger.Shared.Contracts;

namespace Messenger.Domain.Entities;

public sealed class Chat : Entity
{
    public string Title { get; set; } = string.Empty;
    public ChatType Type { get; set; }
    public string? AvatarUrl { get; set; }
    public string? Description { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public ICollection<ChatMembership> Memberships { get; set; } = new List<ChatMembership>();
    public ICollection<Message> Messages { get; set; } = new List<Message>();
    public ICollection<UserFolderChat> FolderLinks { get; set; } = new List<UserFolderChat>();
}
