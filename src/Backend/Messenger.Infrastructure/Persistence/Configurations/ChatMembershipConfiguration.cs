using Messenger.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Messenger.Infrastructure.Persistence.Configurations;

public sealed class ChatMembershipConfiguration : IEntityTypeConfiguration<ChatMembership>
{
    public void Configure(EntityTypeBuilder<ChatMembership> builder)
    {
        builder.ToTable("chat_memberships");

        builder.HasKey(x => new { x.ChatId, x.UserId });
        builder.Property(x => x.ChatId).HasColumnName("chat_id");
        builder.Property(x => x.UserId).HasColumnName("user_id");
        builder.Property(x => x.Role).HasColumnName("role").IsRequired();
        builder.Property(x => x.JoinedAtUtc).HasColumnName("joined_at_utc").IsRequired();
        builder.Property(x => x.LastReadAtUtc).HasColumnName("last_read_at_utc");
        builder.Property(x => x.IsMuted).HasColumnName("is_muted").IsRequired();

        builder.HasOne(x => x.Chat)
            .WithMany(x => x.Memberships)
            .HasForeignKey(x => x.ChatId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.User)
            .WithMany(x => x.Memberships)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
