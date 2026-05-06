// Клиентское E2E: «ChatSafetyNumberStore» (сессии, ключи, ratchet).
using System.Security.Cryptography;
using System.Text;

namespace Messenger.Client.Avalonia.Services.Crypto;

public sealed class ChatSafetyNumberStore(
    ILocalCacheService localCacheService,
    IKeyStore keyStore) : IChatSafetyNumberStore
{
    private const string CacheKey = "chat-safety-number-dpapi-v1";

    public async Task<string?> GetOrCreateAsync(Guid userId, Guid chatId, bool canCreate, CancellationToken ct = default)
    {
        var map = await localCacheService.LoadAsync<Dictionary<string, string>>(CacheKey, ct)
                  ?? new Dictionary<string, string>();
        var key = BuildKey(userId, chatId);

        if (map.TryGetValue(key, out var blob))
        {
            try
            {
                return Encoding.UTF8.GetString(keyStore.UnprotectFromBase64(blob));
            }
            catch
            {
                map.Remove(key);
                await localCacheService.SaveAsync(CacheKey, map, ct);
            }
        }

        if (!canCreate)
            return null;

        var created = ComputeDeterministicChatSafety(chatId);
        map[key] = keyStore.ProtectToBase64(Encoding.UTF8.GetBytes(created));
        await localCacheService.SaveAsync(CacheKey, map, ct);
        return created;
    }

    public async Task<string> RegenerateAsync(Guid userId, Guid chatId, bool canCreate, CancellationToken ct = default)
    {
        if (!canCreate)
            throw new InvalidOperationException("Only allowed chat owners can regenerate this chat safety number.");

        var map = await localCacheService.LoadAsync<Dictionary<string, string>>(CacheKey, ct)
                  ?? new Dictionary<string, string>();
        var key = BuildKey(userId, chatId);
        var regenerated = GenerateRandomSafety();
        map[key] = keyStore.ProtectToBase64(Encoding.UTF8.GetBytes(regenerated));
        await localCacheService.SaveAsync(CacheKey, map, ct);
        return regenerated;
    }

    private static string BuildKey(Guid userId, Guid chatId) =>
        $"{userId:D}:{chatId:D}".ToLowerInvariant();

    private static string ComputeDeterministicChatSafety(Guid chatId)
    {
        var digest = SHA512.HashData(Encoding.UTF8.GetBytes($"chat-safety:{chatId:D}".ToLowerInvariant()));
        return ToDigits(digest);
    }

    private static string GenerateRandomSafety()
    {
        Span<byte> bytes = stackalloc byte[60];
        RandomNumberGenerator.Fill(bytes);
        return ToDigits(bytes.ToArray());
    }

    private static string ToDigits(byte[] bytes)
    {
        var groups = new string[12];
        for (int i = 0; i < 12; i++)
        {
            ulong chunk = 0;
            for (int j = 0; j < 5; j++)
                chunk = (chunk << 8) | bytes[i * 5 + j];
            groups[i] = (chunk % 100_000UL).ToString("D5");
        }
        return string.Join(' ', groups);
    }
}
