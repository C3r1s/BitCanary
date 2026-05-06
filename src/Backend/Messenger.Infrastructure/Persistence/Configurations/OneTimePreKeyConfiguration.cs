// Конфигурация сущности EF Core «OneTimePreKeyConfiguration»: индексы, связи и ограничения.
using Messenger.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Messenger.Infrastructure.Persistence.Configurations;

public sealed class OneTimePreKeyConfiguration : IEntityTypeConfiguration<OneTimePreKey>
{
    public void Configure(EntityTypeBuilder<OneTimePreKey> builder)
    {
        builder.ToTable("one_time_pre_keys");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(x => x.PublicKey).HasColumnName("public_key").IsRequired();
        builder.Property(x => x.ClaimedAt).HasColumnName("claimed_at");

        builder.HasIndex(x => new { x.UserId, x.ClaimedAt });
    }
}
