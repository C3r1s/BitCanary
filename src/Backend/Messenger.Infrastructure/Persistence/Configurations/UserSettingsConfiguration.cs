// Конфигурация сущности EF Core «UserSettingsConfiguration»: индексы, связи и ограничения.
using Messenger.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Messenger.Infrastructure.Persistence.Configurations;

public sealed class UserSettingsConfiguration : IEntityTypeConfiguration<UserSettings>
{
    public void Configure(EntityTypeBuilder<UserSettings> builder)
    {
        builder.ToTable("user_settings");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(x => x.ThemePreference).HasColumnName("theme_preference").IsRequired();
        builder.Property(x => x.SendByEnter).HasColumnName("send_by_enter").IsRequired();
        builder.Property(x => x.UseCompactMode).HasColumnName("use_compact_mode").IsRequired();
        builder.Property(x => x.EnableCustomEmoji).HasColumnName("enable_custom_emoji").IsRequired();
        builder.Property(x => x.ShowNotifications)
            .HasColumnName("show_notifications")
            .HasDefaultValue(true)
            .IsRequired();
        builder.Property(x => x.ShowSenderName)
            .HasColumnName("show_sender_name")
            .HasDefaultValue(true)
            .IsRequired();
        builder.Property(x => x.TerminalColorScheme)
            .HasColumnName("terminal_color_scheme")
            .HasDefaultValue(Messenger.Shared.Contracts.TerminalColorScheme.MatrixGreen)
            .IsRequired();

        builder.HasIndex(x => x.UserId).IsUnique();
    }
}
