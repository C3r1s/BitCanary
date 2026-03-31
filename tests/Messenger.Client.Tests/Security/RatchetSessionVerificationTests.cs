using Messenger.Client.Avalonia.Services;
using Messenger.Client.Avalonia.Services.Crypto;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Messenger.Client.Tests.Security;

/// <summary>
/// Integration tests for verification state persistence in IRatchetSessionRepository.
/// Uses an in-memory SQLite database to exercise the new columns.
/// Covers KEY-01 persistence: verified, last_verified_at, remote_ik_public round-trips.
/// </summary>
public sealed class RatchetSessionVerificationTests : IAsyncLifetime
{
    private SqliteConnection _conn = null!;
    private IRatchetSessionRepository _repo = null!;

    public async Task InitializeAsync()
    {
        _conn = new SqliteConnection("Data Source=:memory:");
        await _conn.OpenAsync();

        // Apply schema (creates ratchet_sessions + schema_migrations + MigrateToV2)
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
        // Arrange
        const string sessionId = "test-session-verify-true";
        var timestamp = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

        // Act — write verified=true with a timestamp
        await _repo.SaveVerificationStateAsync(sessionId, verified: true, lastVerifiedAt: timestamp, remoteIkPublic: null);

        // Assert — read back and confirm
        var (verified, lastVerifiedAt, _) = await _repo.LoadVerificationStateAsync(sessionId);
        Assert.True(verified);
        Assert.NotNull(lastVerifiedAt);
        Assert.Equal(timestamp.UtcDateTime, lastVerifiedAt!.Value.UtcDateTime);
    }

    [Fact]
    public async Task SaveAndLoad_RemoteIkPublic_RoundTrips()
    {
        // Arrange
        const string sessionId = "test-session-ik-roundtrip";
        var ikPublic = new byte[32];
        for (int i = 0; i < 32; i++) ikPublic[i] = (byte)(i + 1);

        // Act — write remote IK
        await _repo.SaveVerificationStateAsync(sessionId, verified: false, lastVerifiedAt: null, remoteIkPublic: ikPublic);

        // Assert — read back and confirm exact bytes
        var (_, _, remoteIk) = await _repo.LoadVerificationStateAsync(sessionId);
        Assert.NotNull(remoteIk);
        Assert.True(ikPublic.SequenceEqual(remoteIk!));
    }

    [Fact]
    public async Task NewSession_DefaultsToUnverified()
    {
        // Arrange — session ID that was never written
        const string sessionId = "test-session-never-written";

        // Act
        var (verified, lastVerifiedAt, remoteIk) = await _repo.LoadVerificationStateAsync(sessionId);

        // Assert — must return (false, null, null) — no false-positive alert
        Assert.False(verified);
        Assert.Null(lastVerifiedAt);
        Assert.Null(remoteIk);
    }
}
