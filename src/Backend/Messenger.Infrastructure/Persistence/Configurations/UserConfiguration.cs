using Messenger.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Messenger.Infrastructure.Persistence.Configurations;

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.UserName).HasColumnName("username").HasMaxLength(64).IsRequired();
        builder.Property(x => x.DisplayName).HasColumnName("display_name").HasMaxLength(128).IsRequired();
        builder.Property(x => x.PasswordHash).HasColumnName("password_hash").HasMaxLength(256).IsRequired();
        builder.Property(x => x.PublicKey).HasColumnName("public_key").HasColumnType("text").IsRequired();
        builder.Property(x => x.Bio).HasColumnName("bio").HasMaxLength(512);
        builder.Property(x => x.AvatarUrl).HasColumnName("avatar_url").HasMaxLength(512);
        builder.Property(x => x.LastSeenUtc).HasColumnName("last_seen_utc");
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();

        builder.HasIndex(x => x.UserName).IsUnique();

        builder.HasOne(x => x.Settings)
            .WithOne(x => x.User)
            .HasForeignKey<UserSettings>(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
