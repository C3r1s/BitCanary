// Доменная сущность «MediaAsset»: модель данных для персистентности BitCanary.
using Messenger.Domain.Abstractions;

namespace Messenger.Domain.Entities;

public sealed class MediaAsset : Entity
{
    public Guid UploadedByUserId { get; set; }
    public User UploadedByUser { get; set; } = null!;
    public string BlobPath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public DateTimeOffset UploadedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public ICollection<Message> Messages { get; set; } = new List<Message>();
}
