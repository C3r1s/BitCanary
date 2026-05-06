// Автотест BitCanary: проверка «RatchetSessionVerificationTests».
using Messenger.Client.Avalonia.Services;
using Messenger.Client.Avalonia.Services.Crypto;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Messenger.Client.Tests.Security;

public sealed class RatchetSessionVerificationTests : IAsyncLifetime
{
    private SqliteConnection _conn = null!;
    private IRatchetSessionRepository _repo = null!;

    public async Task InitializeAsync()
    {
        _conn = new SqliteConnection("Data Source=:memory:");
        await _conn.OpenAsync();

        await DatabaseService.ApplySchemaForTestAsync(_conn);

        _repo = new RatchetSessionRepository(_conn);
    }

    public async Task DisposeAsync()
    {
        await _conn.DisposeAsync();
    }

    [Fact]
    public async Task SaveAndLoad_VerifiedTrue_RoundTrips()
    {
        const string sessionId = "test-session-verify-true";
        var timestamp = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

        await _repo.SaveVerificationStateAsync(sessionId, verified: true, lastVerifiedAt: timestamp, remoteIkPublic: null);

        var (verified, lastVerifiedAt, _) = await _repo.LoadVerificationStateAsync(sessionId);
        Assert.True(verified);
        Assert.NotNull(lastVerifiedAt);
        Assert.Equal(timestamp.UtcDateTime, lastVerifiedAt!.Value.UtcDateTime);
    }

    [Fact]
    public async Task SaveAndLoad_RemoteIkPublic_RoundTrips()
    {
        const string sessionId = "test-session-ik-roundtrip";
        var ikPublic = new byte[32];
        for (int i = 0; i < 32; i++) ikPublic[i] = (byte)(i + 1);

        await _repo.SaveVerificationStateAsync(sessionId, verified: false, lastVerifiedAt: null, remoteIkPublic: ikPublic);

        var (_, _, remoteIk) = await _repo.LoadVerificationStateAsync(sessionId);
        Assert.NotNull(remoteIk);
        Assert.True(ikPublic.SequenceEqual(remoteIk!));
    }

    [Fact]
    public async Task NewSession_DefaultsToUnverified()
    {
        const string sessionId = "test-session-never-written";

        var (verified, lastVerifiedAt, remoteIk) = await _repo.LoadVerificationStateAsync(sessionId);

        Assert.False(verified);
        Assert.Null(lastVerifiedAt);
        Assert.Null(remoteIk);
    }
}
