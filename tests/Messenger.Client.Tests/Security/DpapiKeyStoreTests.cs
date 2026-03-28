using System.Security.Cryptography;
using Messenger.Client.Avalonia.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Messenger.Client.Tests.Security;

[Trait("Category", "Unit")]
public sealed class DpapiKeyStoreTests
{
    private static DpapiKeyStore CreateStore()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        var provider = new ServiceCollection()
            .AddDataProtection()
            .SetApplicationName("Messenger.Test")
            .PersistKeysToFileSystem(new DirectoryInfo(tempDir))
            .Services
            .BuildServiceProvider()
            .GetRequiredService<IDataProtectionProvider>();
        return new DpapiKeyStore(provider);
    }

    [Fact]
    public void RoundTrip_Protect_Unprotect_ReturnsOriginalBytes()
    {
        var store = CreateStore();
        var original = new byte[] { 1, 2, 3, 4 };
        var protected_ = store.Protect(original);
        var restored = store.Unprotect(protected_);
        Assert.Equal(original, restored);
    }

    [Fact]
    public void Base64RoundTrip_ProtectToBase64_UnprotectFromBase64_ReturnsOriginalBytes()
    {
        var store = CreateStore();
        var key = RandomNumberGenerator.GetBytes(32);
        var base64Blob = store.ProtectToBase64(key);
        var restored = store.UnprotectFromBase64(base64Blob);
        Assert.Equal(key, restored);
    }

    [Fact]
    public void TamperedBlob_Unprotect_ThrowsCryptographicException()
    {
        var store = CreateStore();
        var blob = store.Protect(new byte[] { 1 });
        blob[blob.Length / 2] ^= 0xFF;
        Assert.Throws<CryptographicException>(() => store.Unprotect(blob));
    }
}
