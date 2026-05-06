// Сервис клиента BitCanary: сеть, кэш, медиа — «StartupMigrationService».
using Messenger.Shared.Contracts.Dtos;
using System.Security.Cryptography;

namespace Messenger.Client.Avalonia.Services;

public sealed class StartupMigrationService(
    IKeyStore keyStore,
    ILocalCacheService localCacheService,
    ILocalMessageRepository messageRepository)
{
    private static readonly string LocalAppData = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Messenger.Client.Avalonia");

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        await MigrateKeyRingAsync(cancellationToken);
        await MigrateChatsAsync(cancellationToken);
        AssertNoPlaintextKeyRing();
    }

    private async Task MigrateKeyRingAsync(CancellationToken cancellationToken)
    {
        var existing = await localCacheService.LoadAsync<Dictionary<string, string>>(
            "encryption-keyring", cancellationToken);
        if (existing is null) return;

        var protected_ = new Dictionary<string, string>();
        foreach (var (envelopeId, base64RawKey) in existing)
        {
            var rawKey = Convert.FromBase64String(base64RawKey);
            protected_[envelopeId] = keyStore.ProtectToBase64(rawKey);
            CryptographicOperations.ZeroMemory(rawKey);
        }

        await localCacheService.SaveAsync("encryption-keyring-dpapi", protected_, cancellationToken);

        var plaintextFile = Path.Combine(LocalAppData, "encryption-keyring.json");
        if (File.Exists(plaintextFile))
            File.Delete(plaintextFile);
    }

    private async Task MigrateChatsAsync(CancellationToken cancellationToken)
    {
        var chats = await localCacheService.LoadAsync<List<ChatSummaryDto>>("chats", cancellationToken);
        if (chats is null || chats.Count == 0) return;

        const int batchSize = 100;
        for (var i = 0; i < chats.Count; i += batchSize)
        {
            var batch = chats.Skip(i).Take(batchSize);
            foreach (var chat in batch)
            {
                await messageRepository.UpsertChatAsync(chat, cancellationToken);
            }
            await Task.Delay(10, cancellationToken);
        }

        await localCacheService.SaveAsync("chats", new List<ChatSummaryDto>(), cancellationToken);
    }

    private static void AssertNoPlaintextKeyRing()
    {
        if (!Directory.Exists(LocalAppData)) return;

        foreach (var file in Directory.EnumerateFiles(LocalAppData, "encryption-keyring*.json"))
        {
            using var fs = File.OpenRead(file);
            var header = new byte[2];
            if (fs.Read(header, 0, 2) == 2 && header[0] == (byte)'{')
                throw new InvalidOperationException(
                    $"Unprotected key material detected on disk: {file}. " +
                    "The keyring must be DPAPI-protected before the app can start.");
        }
    }
}
