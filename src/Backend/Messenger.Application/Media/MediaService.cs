// Сохранение загруженных файлов и выдача потоков для вложений в сообщениях.
using System.Net;
using Messenger.Application.Abstractions;
using Messenger.Application.Common;
using Messenger.Domain.Entities;
using Messenger.Shared.Contracts.Dtos;
using Microsoft.EntityFrameworkCore;

namespace Messenger.Application.Media;

public sealed class MediaService(
    IAppDbContext dbContext,
    ICurrentUserContext currentUser,
    IStorageService storageService) : IMediaService
{
    public async Task<MediaUploadResponse> UploadAsync(
        string fileName,
        string contentType,
        long sizeBytes,
        Stream content,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireUserId();

        var blobPath = await storageService.SaveAsync(content, fileName, contentType, cancellationToken);

        var mediaAsset = new MediaAsset
        {
            UploadedByUserId = userId,
            BlobPath = blobPath,
            FileName = fileName,
            ContentType = contentType,
            SizeBytes = sizeBytes
        };

        dbContext.MediaAssets.Add(mediaAsset);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new MediaUploadResponse(mediaAsset.Id, mediaAsset.BlobPath, mediaAsset.FileName, mediaAsset.ContentType, mediaAsset.SizeBytes);
    }

    public async Task<MediaDownloadResult> DownloadAsync(Guid mediaId, CancellationToken cancellationToken)
    {
        currentUser.RequireUserId();

        var asset = await dbContext.MediaAssets
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == mediaId, cancellationToken)
            ?? throw new AppException("Media asset not found.", HttpStatusCode.NotFound);

        var stream = await storageService.GetAsync(asset.BlobPath, cancellationToken);
        return new MediaDownloadResult(stream, asset.ContentType, asset.FileName);
    }
}
