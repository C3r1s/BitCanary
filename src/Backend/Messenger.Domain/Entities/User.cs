using Messenger.Domain.Abstractions;

namespace Messenger.Domain.Entities;

public sealed class User : Entity
{
    public string UserName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string PublicKey { get; set; } = string.Empty;
    public string? Bio { get; set; }
    public string? AvatarUrl { get; set; }
    public DateTimeOffset? LastSeenUtc { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public UserSettings? Settings { get; set; }
    public ICollection<ChatMembership> Memberships { get; set; } = new List<ChatMembership>();
    public ICollection<Message> SentMessages { get; set; } = new List<Message>();
    public ICollection<Folder> Folders { get; set; } = new List<Folder>();
    public ICollection<MediaAsset> UploadedMediaAssets { get; set; } = new List<MediaAsset>();
}
