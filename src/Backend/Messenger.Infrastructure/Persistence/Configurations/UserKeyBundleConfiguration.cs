using Messenger.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Messenger.Infrastructure.Persistence.Configurations;

public sealed class UserKeyBundleConfiguration : IEntityTypeConfiguration<UserKeyBundle>
{
    public void Configure(EntityTypeBuilder<UserKeyBundle> builder)
    {
        builder.ToTable("user_key_bundles");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(x => x.DeviceId).HasColumnName("device_id").IsRequired();
        builder.Property(x => x.IkPublic).HasColumnName("ik_public").IsRequired();
        builder.Property(x => x.SpkPublic).HasColumnName("spk_public").IsRequired();
        builder.Property(x => x.SpkSignature).HasColumnName("spk_signature").IsRequired();
        builder.Property(x => x.SpkCreatedAt).HasColumnName("spk_created_at").IsRequired();

        builder.HasIndex(x => new { x.UserId, x.DeviceId }).IsUnique();
    }
}
