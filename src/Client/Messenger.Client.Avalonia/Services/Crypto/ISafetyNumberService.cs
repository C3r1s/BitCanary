// Клиентское E2E: «ISafetyNumberService» (сессии, ключи, ratchet).
namespace Messenger.Client.Avalonia.Services.Crypto;

public interface ISafetyNumberService
{
    string Compute(byte[] localIkPublic, string localUserId,
                   byte[] remoteIkPublic, string remoteUserId);
}
