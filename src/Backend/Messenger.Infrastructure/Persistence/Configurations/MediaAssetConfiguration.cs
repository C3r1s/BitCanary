using Messenger.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Messenger.Infrastructure.Persistence.Configurations;

public sealed class MediaAssetConfiguration : IEntityTypeConfiguration<MediaAsset>
{
    public void Configure(EntityTypeBuilder<MediaAsset> builder)
    {
        builder.ToTable("media_assets");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.UploadedByUserId).HasColumnName("uploaded_by_user_id").IsRequired();
        builder.Property(x => x.BlobPath).HasColumnName("blob_path").HasMaxLength(512).IsRequired();
        builder.Property(x => x.FileName).HasColumnName("file_name").HasMaxLength(256).IsRequired();
        builder.Property(x => x.ContentType).HasColumnName("content_type").HasMaxLength(256).IsRequired();
        builder.Property(x => x.SizeBytes).HasColumnName("size_bytes").IsRequired();
        builder.Property(x => x.UploadedAtUtc).HasColumnName("uploaded_at_utc").IsRequired();

        builder.HasOne(x => x.UploadedByUser)
            .WithMany(x => x.UploadedMediaAssets)
            .HasForeignKey(x => x.UploadedByUserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
