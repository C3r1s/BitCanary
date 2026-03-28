using Messenger.Domain.Abstractions;

namespace Messenger.Domain.Entities;

public sealed class Folder : Entity
{
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public string Name { get; set; } = string.Empty;
    public int Order { get; set; }
    public ICollection<UserFolderChat> ChatLinks { get; set; } = new List<UserFolderChat>();
}
