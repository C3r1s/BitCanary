// EF Core контекст PostgreSQL: таблицы пользователей, чатов, сообщений и ключей.
using Messenger.Application.Abstractions;
using Messenger.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Messenger.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options), IAppDbContext
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Chat> Chats => Set<Chat>();
    public DbSet<ChatMembership> ChatMemberships => Set<ChatMembership>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<Folder> Folders => Set<Folder>();
    public DbSet<UserFolderChat> UserFolderChats => Set<UserFolderChat>();
    public DbSet<UserSettings> UserSettings => Set<UserSettings>();
    public DbSet<MediaAsset> MediaAssets => Set<MediaAsset>();
    public DbSet<CallSignalLog> CallSignalLogs => Set<CallSignalLog>();
    public DbSet<UserKeyBundle> UserKeyBundles => Set<UserKeyBundle>();
    public DbSet<OneTimePreKey> OneTimePreKeys => Set<OneTimePreKey>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
