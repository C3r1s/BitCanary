// Конфигурация сущности EF Core «MessageConfiguration»: индексы, связи и ограничения.
using Messenger.Domain.Entities;
using Messenger.Shared.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Messenger.Infrastructure.Persistence.Configurations;

public sealed class MessageConfiguration : IEntityTypeConfiguration<Message>
{
    public void Configure(EntityTypeBuilder<Message> builder)
    {
        builder.ToTable("messages");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.ChatId).HasColumnName("chat_id").IsRequired();
        builder.Property(x => x.SenderId).HasColumnName("sender_id").IsRequired();
        builder.Property(x => x.ClientMessageId).HasColumnName("client_message_id").IsRequired();
        builder.Property(x => x.Kind).HasColumnName("kind").IsRequired();
        builder.Property(x => x.EncryptedPayload).HasColumnName("encrypted_payload").HasColumnType("text").IsRequired();
        builder.Property(x => x.EncryptionAlgorithm).HasColumnName("encryption_algorithm").HasMaxLength(128).IsRequired();
        builder.Property(x => x.KeyEnvelope).HasColumnName("key_envelope").HasColumnType("text").IsRequired();
        builder.Property(x => x.MediaId).HasColumnName("media_id");
        builder.Property(x => x.ReplyToMessageId).HasColumnName("reply_to_message_id");
        builder.Property(x => x.MetadataJson).HasColumnName("metadata_json").HasColumnType("jsonb");
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        builder.Property(x => x.ProtocolVersion).HasColumnName("protocol_version").HasDefaultValue(ProtocolVersion.LegacyAes).IsRequired();

        builder.HasIndex(x => new { x.ChatId, x.CreatedAtUtc });
        builder.HasIndex(x => new { x.ChatId, x.SenderId, x.ClientMessageId }).IsUnique();

        builder.HasOne(x => x.Chat)
            .WithMany(x => x.Messages)
            .HasForeignKey(x => x.ChatId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.Sender)
            .WithMany(x => x.SentMessages)
            .HasForeignKey(x => x.SenderId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Media)
            .WithMany(x => x.Messages)
            .HasForeignKey(x => x.MediaId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(x => x.ReplyToMessage)
            .WithMany(x => x.Replies)
            .HasForeignKey(x => x.ReplyToMessageId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
