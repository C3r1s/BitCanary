// Локальное файловое хранилище вложений на диске сервера.
using Messenger.Application.Abstractions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Messenger.Infrastructure.Services;

public sealed class LocalFileStorageService(
    IHostEnvironment hostEnvironment,
    IOptions<StorageOptions> storageOptions) : IStorageService
{
    private readonly StorageOptions _options = storageOptions.Value;

    public async Task<string> SaveAsync(Stream content, string fileName, string contentType, CancellationToken cancellationToken)
    {
        var safeFileName = Path.GetFileName(fileName);
        var extension = Path.GetExtension(safeFileName);
        var blobName = $"{Guid.NewGuid():N}{extension}";
        var root = Path.IsPathRooted(_options.RootPath)
            ? _options.RootPath
            : Path.Combine(hostEnvironment.ContentRootPath, _options.RootPath);

        Directory.CreateDirectory(root);

        var fullPath = Path.Combine(root, blobName);

        await using var fileStream = File.Create(fullPath);
        await content.CopyToAsync(fileStream, cancellationToken);

        return blobName;
    }

    public Task<Stream> GetAsync(string blobPath, CancellationToken cancellationToken)
    {
        var root = Path.IsPathRooted(_options.RootPath)
            ? _options.RootPath
            : Path.Combine(hostEnvironment.ContentRootPath, _options.RootPath);

        var fullPath = Path.GetFullPath(Path.Combine(root, Path.GetFileName(blobPath)));
        if (!fullPath.StartsWith(Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException("Invalid blob path.");
        }

        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("Media file not found.", blobPath);
        }

        Stream stream = File.OpenRead(fullPath);
        return Task.FromResult(stream);
    }
}
