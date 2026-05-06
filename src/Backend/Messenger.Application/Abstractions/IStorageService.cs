// Абстракция слоя Application BitCanary: «IStorageService».
namespace Messenger.Application.Abstractions;

public interface IStorageService
{
    Task<string> SaveAsync(Stream content, string fileName, string contentType, CancellationToken cancellationToken);

    Task<Stream> GetAsync(string blobPath, CancellationToken cancellationToken);
}
