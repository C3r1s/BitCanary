using System.Security.Cryptography;

namespace Messenger.Client.Avalonia.Services;

/// <summary>
/// Runs once on startup. Migrates any plaintext encryption-keyring.json entries to
/// DPAPI-protected blobs and asserts no unprotected key material remains on disk.
/// </summary>
public sealed class StartupMigrationService(
    IKeyStore keyStore,
    ILocalCacheService localCacheService)
{
    private static readonly string LocalAppData = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Messenger.Client.Avalonia");

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        await MigrateKeyRingAsync(cancellationToken);
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

    private static void AssertNoPlaintextKeyRing()
    {
        if (!Directory.Exists(LocalAppData)) return;

        foreach (var file in Directory.EnumerateFiles(LocalAppData, "encryption-keyring*.json"))
        {
            // Read first 2 bytes to detect JSON object (plaintext)
            using var fs = File.OpenRead(file);
            var header = new byte[2];
            if (fs.Read(header, 0, 2) == 2 && header[0] == (byte)'{')
                throw new InvalidOperationException(
                    $"Unprotected key material detected on disk: {file}. " +
                    "The keyring must be DPAPI-protected before the app can start.");
        }
    }
}
