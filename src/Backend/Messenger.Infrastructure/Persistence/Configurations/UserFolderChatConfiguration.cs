using Messenger.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Messenger.Infrastructure.Persistence.Configurations;

public sealed class UserFolderChatConfiguration : IEntityTypeConfiguration<UserFolderChat>
{
    public void Configure(EntityTypeBuilder<UserFolderChat> builder)
    {
        builder.ToTable("folder_chats");

        builder.HasKey(x => new { x.FolderId, x.ChatId });
        builder.Property(x => x.FolderId).HasColumnName("folder_id");
        builder.Property(x => x.ChatId).HasColumnName("chat_id");

        builder.HasOne(x => x.Folder)
            .WithMany(x => x.ChatLinks)
            .HasForeignKey(x => x.FolderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.Chat)
            .WithMany(x => x.FolderLinks)
            .HasForeignKey(x => x.ChatId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
