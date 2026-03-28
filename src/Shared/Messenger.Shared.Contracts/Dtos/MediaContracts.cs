namespace Messenger.Shared.Contracts.Dtos;

public sealed record MediaUploadResponse(
    Guid MediaId,
    string BlobPath,
    string FileName,
    string ContentType,
    long SizeBytes);
