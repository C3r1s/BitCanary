using System.Text.Json;
using Messenger.Client.Avalonia.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Messenger.Client.Tests.Security;

[Trait("Category", "Unit")]
public sealed class ClientSessionServiceTests : IDisposable
{
    private static readonly string SessionFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Messenger.Client.Avalonia",
        "session.json");

    private readonly string _dpKeysDir;

    public ClientSessionServiceTests()
    {
        // Ensure a clean slate — delete any leftover session.json from a prior run.
        if (File.Exists(SessionFilePath)) File.Delete(SessionFilePath);

        _dpKeysDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_dpKeysDir);
    }

    public void Dispose()
    {
        if (File.Exists(SessionFilePath)) File.Delete(SessionFilePath);
        try { Directory.Delete(_dpKeysDir, recursive: true); } catch { }
    }

    private IDataProtectionProvider CreateTestProvider()
    {
        return new ServiceCollection()
            .AddDataProtection()
            .SetApplicationName("Messenger.Test")
            .PersistKeysToFileSystem(new DirectoryInfo(_dpKeysDir))
            .Services
            .BuildServiceProvider()
            .GetRequiredService<IDataProtectionProvider>();
    }

    [Fact]
    public void PersistSession_WritesEncryptedBlob_NotPlaintextJwt()
    {
        var provider = CreateTestProvider();
        var svc = new ClientSessionService(provider);
        var fakeJwt = "eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiJhbGljZSJ9.signature";

        svc.SetSession(Guid.NewGuid(), "alice", fakeJwt);

        Assert.True(File.Exists(SessionFilePath));
        var json = File.ReadAllText(SessionFilePath);
        using var doc = JsonDocument.Parse(json);
        var onDiskToken = doc.RootElement.GetProperty("accessToken").GetString();
        Assert.NotNull(onDiskToken);
        Assert.False(onDiskToken!.StartsWith("eyJ", StringComparison.Ordinal),
            "AccessToken on disk must be a DPAPI-protected blob, not a plaintext JWT.");
        Assert.NotEqual(fakeJwt, onDiskToken);
    }

    [Fact]
    public void LoadPersistedSession_RoundTrip_ReturnsOriginalToken()
    {
        var provider = CreateTestProvider();
        var userId = Guid.NewGuid();
        var fakeJwt = "eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiJhbGljZSJ9.signature";

        // Instance A writes
        var svcA = new ClientSessionService(provider);
        svcA.SetSession(userId, "alice", fakeJwt);

        // Instance B reads — same provider = same protector = successful unprotect
        var svcB = new ClientSessionService(provider);

        Assert.True(svcB.IsAuthenticated);
        Assert.Equal(fakeJwt, svcB.AccessToken);
        Assert.Equal(userId, svcB.CurrentUserId);
        Assert.Equal("alice", svcB.UserName);
    }

    [Fact]
    public void LegacyPlaintextFile_IsDetectedAndDeleted()
    {
        // Pre-seed a v1.0 plaintext session file (AccessToken starts with "eyJ").
        Directory.CreateDirectory(Path.GetDirectoryName(SessionFilePath)!);
        var legacyJwt = "eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiJhbGljZSJ9.signature";
        var legacyPayload = $$"""
            {"userId":"{{Guid.NewGuid()}}","userName":"alice","accessToken":"{{legacyJwt}}"}
            """;
        File.WriteAllText(SessionFilePath, legacyPayload);

        var provider = CreateTestProvider();
        var svc = new ClientSessionService(provider);

        Assert.False(File.Exists(SessionFilePath));
        Assert.False(svc.IsAuthenticated);
        Assert.Null(svc.AccessToken);
        Assert.Equal(Guid.Empty, svc.CurrentUserId);
    }

    [Fact]
    public void CorruptBlob_IsDiscardedGracefully()
    {
        // Pre-seed a file with a token that does NOT start with "eyJ" but is also not a valid DPAPI blob.
        Directory.CreateDirectory(Path.GetDirectoryName(SessionFilePath)!);
        var corruptPayload = $$"""
            {"userId":"{{Guid.NewGuid()}}","userName":"alice","accessToken":"not-base64-not-a-jwt-just-garbage"}
            """;
        File.WriteAllText(SessionFilePath, corruptPayload);

        var provider = CreateTestProvider();
        var svc = new ClientSessionService(provider);

        Assert.False(File.Exists(SessionFilePath));
        Assert.False(svc.IsAuthenticated);
    }
}
