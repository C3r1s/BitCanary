using Messenger.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Messenger.Application.Abstractions;

public interface IAppDbContext
{
    DbSet<User> Users { get; }
    DbSet<Chat> Chats { get; }
    DbSet<ChatMembership> ChatMemberships { get; }
    DbSet<Message> Messages { get; }
    DbSet<Folder> Folders { get; }
    DbSet<UserFolderChat> UserFolderChats { get; }
    DbSet<UserSettings> UserSettings { get; }
    DbSet<MediaAsset> MediaAssets { get; }
    DbSet<CallSignalLog> CallSignalLogs { get; }
    DbSet<UserKeyBundle> UserKeyBundles { get; }
    DbSet<OneTimePreKey> OneTimePreKeys { get; }
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
