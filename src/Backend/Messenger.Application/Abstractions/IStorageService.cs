namespace Messenger.Application.Abstractions;

public interface IStorageService
{
    Task<string> SaveAsync(Stream content, string fileName, string contentType, CancellationToken cancellationToken);

    /// <summary>Returns a readable stream for the given blob path.</summary>
    Task<Stream> GetAsync(string blobPath, CancellationToken cancellationToken);
}
