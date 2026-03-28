namespace Messenger.Client.Avalonia.Services;

public interface ILocalCacheService
{
    Task SaveAsync<T>(string key, T data, CancellationToken cancellationToken = default);
    Task<T?> LoadAsync<T>(string key, CancellationToken cancellationToken = default);
}
