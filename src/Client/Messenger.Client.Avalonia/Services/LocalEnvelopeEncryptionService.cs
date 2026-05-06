// Сервис клиента BitCanary: сеть, кэш, медиа — «LocalEnvelopeEncryptionService».
using System.Security.Cryptography;
using System.Text;
using Messenger.Client.Avalonia.Models;
using Messenger.Shared.Contracts;
using Messenger.Shared.Contracts.Dtos;

namespace Messenger.Client.Avalonia.Services;

public sealed class LocalEnvelopeEncryptionService(IKeyStore keyStore, ILocalCacheService localCacheService) : IEncryptionService
{
    private const string EnvelopeCacheKey = "encryption-keyring-dpapi";
    private Dictionary<string, string>? _keyRing;

    public async Task<EncryptedMessageDraft> EncryptTextAsync(string plaintext, Guid recipientUserId, CancellationToken cancellationToken = default)
    {
        await EnsureLoadedAsync(cancellationToken);

        var key = RandomNumberGenerator.GetBytes(32);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        var ciphertext = new byte[plaintextBytes.Length];
        var tag = new byte[16];

        using (var aesGcm = new AesGcm(key, tag.Length))
        {
            aesGcm.Encrypt(nonce, plaintextBytes, ciphertext, tag);
        }

        var envelopeId = Guid.NewGuid().ToString("N");
        _keyRing![envelopeId] = keyStore.ProtectToBase64(key);
        CryptographicOperations.ZeroMemory(key);
        await localCacheService.SaveAsync(EnvelopeCacheKey, _keyRing, cancellationToken);

        var payload = new byte[nonce.Length + tag.Length + ciphertext.Length];
        Buffer.BlockCopy(nonce, 0, payload, 0, nonce.Length);
        Buffer.BlockCopy(tag, 0, payload, nonce.Length, tag.Length);
        Buffer.BlockCopy(ciphertext, 0, payload, nonce.Length + tag.Length, ciphertext.Length);

        return new EncryptedMessageDraft(
            MessageKind.Text,
            Convert.ToBase64String(payload),
            "AES-256-GCM/local-envelope",
            envelopeId,
            null);
    }
    
    public Task<string> DecryptAsync(MessageDto message, CancellationToken cancellationToken = default)
    {
        EnsureLoadedAsync(CancellationToken.None).GetAwaiter().GetResult();

        if (_keyRing is null || !_keyRing.TryGetValue(message.KeyEnvelope, out var base64Blob))
        {
            var result = message.EncryptionAlgorithm.StartsWith("plaintext", StringComparison.OrdinalIgnoreCase)
                ? message.EncryptedPayload
                : "[Encrypted message]";
            return Task.FromResult(result);
        }

        try
        {
            var payload = Convert.FromBase64String(message.EncryptedPayload);
            var nonce = payload[..12];
            var tag = payload[12..28];
            var ciphertext = payload[28..];
            var plaintext = new byte[ciphertext.Length];
            var key = keyStore.UnprotectFromBase64(base64Blob);

            using (var aesGcm = new AesGcm(key, tag.Length))
            {
                aesGcm.Decrypt(nonce, ciphertext, tag, plaintext);
            }

            return Task.FromResult(Encoding.UTF8.GetString(plaintext));
        }
        catch (CryptographicException)
        {
            return Task.FromResult("[Encrypted message]");
        }
    }

    private async Task EnsureLoadedAsync(CancellationToken cancellationToken)
    {
        if (_keyRing is not null)
        {
            return;
        }

        _keyRing = await localCacheService.LoadAsync<Dictionary<string, string>>(EnvelopeCacheKey, cancellationToken)
                   ?? new Dictionary<string, string>();
    }
}
