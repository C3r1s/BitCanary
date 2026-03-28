using System.Text.Json;

namespace Messenger.Client.Avalonia.Services;

public sealed class LocalCacheService : ILocalCacheService
{
    private readonly JsonSerializerOptions _serializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _cacheRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Messenger.Client.Avalonia");

    public async Task SaveAsync<T>(string key, T data, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_cacheRoot);

        var filePath = Path.Combine(_cacheRoot, $"{key}.json");
        await using var stream = File.Create(filePath);
        await JsonSerializer.SerializeAsync(stream, data, _serializerOptions, cancellationToken);
    }

    public async Task<T?> LoadAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        var filePath = Path.Combine(_cacheRoot, $"{key}.json");
        if (!File.Exists(filePath))
        {
            return default;
        }

        await using var stream = File.OpenRead(filePath);
        return await JsonSerializer.DeserializeAsync<T>(stream, _serializerOptions, cancellationToken);
    }
}
