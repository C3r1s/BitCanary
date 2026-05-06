// Доменная сущность «UserFolderChat»: модель данных для персистентности BitCanary.
namespace Messenger.Domain.Entities;

public sealed class UserFolderChat
{
    public Guid FolderId { get; set; }
    public Folder Folder { get; set; } = null!;
    public Guid ChatId { get; set; }
    public Chat Chat { get; set; } = null!;
}
