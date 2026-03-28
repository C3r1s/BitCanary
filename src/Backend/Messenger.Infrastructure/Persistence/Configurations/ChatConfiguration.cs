using Messenger.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Messenger.Infrastructure.Persistence.Configurations;

public sealed class ChatConfiguration : IEntityTypeConfiguration<Chat>
{
    public void Configure(EntityTypeBuilder<Chat> builder)
    {
        builder.ToTable("chats");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.Title).HasColumnName("title").HasMaxLength(256).IsRequired();
        builder.Property(x => x.Type).HasColumnName("type").IsRequired();
        builder.Property(x => x.AvatarUrl).HasColumnName("avatar_url").HasMaxLength(512);
        builder.Property(x => x.Description).HasColumnName("description").HasMaxLength(1024);
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
    }
}
