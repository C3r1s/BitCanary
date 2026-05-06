// Конфигурация сущности EF Core «CallSignalLogConfiguration»: индексы, связи и ограничения.
using Messenger.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Messenger.Infrastructure.Persistence.Configurations;

public sealed class CallSignalLogConfiguration : IEntityTypeConfiguration<CallSignalLog>
{
    public void Configure(EntityTypeBuilder<CallSignalLog> builder)
    {
        builder.ToTable("call_signal_logs");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.ChatId).HasColumnName("chat_id").IsRequired();
        builder.Property(x => x.FromUserId).HasColumnName("from_user_id").IsRequired();
        builder.Property(x => x.ToUserId).HasColumnName("to_user_id").IsRequired();
        builder.Property(x => x.Kind).HasColumnName("kind").IsRequired();
        builder.Property(x => x.Payload).HasColumnName("payload").HasColumnType("text").IsRequired();
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
    }
}
