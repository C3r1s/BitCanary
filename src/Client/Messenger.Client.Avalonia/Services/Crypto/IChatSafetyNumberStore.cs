// Клиентское E2E: «IChatSafetyNumberStore» (сессии, ключи, ratchet).
namespace Messenger.Client.Avalonia.Services.Crypto;

public interface IChatSafetyNumberStore
{
    Task<string?> GetOrCreateAsync(Guid userId, Guid chatId, bool canCreate, CancellationToken ct = default);
    Task<string> RegenerateAsync(Guid userId, Guid chatId, bool canCreate, CancellationToken ct = default);
}
