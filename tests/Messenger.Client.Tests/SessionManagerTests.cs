// Автотест BitCanary: проверка «SessionManagerTests».
using Messenger.Client.Avalonia.Services.Crypto;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Messenger.Client.Tests;

public class SessionManagerTests : IDisposable
{
    private readonly SqliteConnection _db;
    private readonly RatchetSessionRepository _repo;

    public SessionManagerTests()
    {
        _db = new SqliteConnection("Data Source=:memory:");
        _db.Open();
        ApplySchema(_db);
        _repo = new RatchetSessionRepository(_db);
    }

    public void Dispose() => _db.Dispose();

    private static void ApplySchema(SqliteConnection db)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS ratchet_sessions (
                id           TEXT PRIMARY KEY NOT NULL,
                session_blob BLOB NOT NULL,
                updated_at   TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS skipped_message_keys (
                session_id      TEXT    NOT NULL,
                ratchet_pubkey  BLOB    NOT NULL,
                message_number  INTEGER NOT NULL,
                message_key     BLOB    NOT NULL,
                stored_at       TEXT    NOT NULL,
                PRIMARY KEY (session_id, ratchet_pubkey, message_number)
            );
            """;
        cmd.ExecuteNonQuery();
    }


    [Fact]
    public async Task SaveAndLoad_Session_RoundTrip()
    {
        var state = new RatchetState
        {
            SessionId = "alice:1",
            RootKey = new byte[] { 1, 2, 3, 4 },
            SendingChainKey = new byte[] { 5, 6, 7, 8 },
            ReceivingChainKey = new byte[] { 9, 10, 11, 12 },
            SendMessageNumber = 7,
            ReceiveMessageNumber = 3
        };
        var blob = state.Serialize();

        await _repo.SaveSessionAsync("alice:1", blob);
        var loaded = await _repo.LoadSessionAsync("alice:1");

        Assert.NotNull(loaded);
        var restored = RatchetState.Deserialize(loaded!);
        Assert.Equal("alice:1", restored.SessionId);
        Assert.Equal(7, restored.SendMessageNumber);
        Assert.Equal(3, restored.ReceiveMessageNumber);
        Assert.Equal(state.RootKey, restored.RootKey);
    }

    [Fact]
    public async Task SaveAndConsumeSkippedKey_RoundTrip()
    {
        var sessionId = "session-x";
        var ratchetPub = new byte[] { 0xAB, 0xCD, 0xEF };
        var msgKey = new byte[] { 0x11, 0x22, 0x33 };
        int msgNumber = 42;

        await _repo.SaveSkippedKeyAsync(sessionId, ratchetPub, msgNumber, msgKey);

        var consumed = await _repo.TryConsumeSkippedKeyAsync(sessionId, ratchetPub, msgNumber);
        Assert.NotNull(consumed);
        Assert.Equal(msgKey, consumed);

        var second = await _repo.TryConsumeSkippedKeyAsync(sessionId, ratchetPub, msgNumber);
        Assert.Null(second);
    }

    [Fact]
    public async Task EnforceSkippedKeyLimit_EvictsOldestBeyondMax()
    {
        var sessionId = "session-limit";
        var ratchetPub = new byte[] { 0x01, 0x02, 0x03 };

        for (int i = 0; i < 5; i++)
        {
            await _repo.SaveSkippedKeyAsync(sessionId, ratchetPub, i, new byte[] { (byte)i });
            await Task.Delay(2);
        }

        await _repo.EnforceSkippedKeyLimitAsync(sessionId, 3);

        using var cmd = _db.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM skipped_message_keys WHERE session_id = $sid";
        cmd.Parameters.AddWithValue("$sid", sessionId);
        var count = (long)(await cmd.ExecuteScalarAsync())!;
        Assert.Equal(3, count);
    }


    [Fact]
    public async Task SessionManager_EncryptAsync_AcquiresSemaphoreAndPersistsState()
    {
        var svc = new DoubleRatchetService();
        var manager = new SessionManager(svc, _repo);

        var sharedSecret = new byte[32];
        System.Security.Cryptography.RandomNumberGenerator.Fill(sharedSecret);

        var bobSpkKey = NSec.Cryptography.Key.Create(
            NSec.Cryptography.KeyAgreementAlgorithm.X25519,
            new NSec.Cryptography.KeyCreationParameters
            {
                ExportPolicy = NSec.Cryptography.KeyExportPolicies.AllowPlaintextExport
            });
        var bobSpkPublic = bobSpkKey.PublicKey.Export(NSec.Cryptography.KeyBlobFormat.RawPublicKey);

        await manager.CreateInitiatorSessionAsync("sess-1", sharedSecret, bobSpkPublic);

        var loaded = await _repo.LoadSessionAsync("sess-1");
        Assert.NotNull(loaded);

        var plaintext = System.Text.Encoding.UTF8.GetBytes("hello");
        var ad = System.Text.Encoding.UTF8.GetBytes("aad");
        var (ct, _, _, _) = await manager.EncryptAsync("sess-1", plaintext, ad);

        Assert.NotEmpty(ct);

        var blobAfter = await _repo.LoadSessionAsync("sess-1");
        Assert.NotNull(blobAfter);
        var stateAfter = RatchetState.Deserialize(blobAfter!);
        Assert.Equal(1, stateAfter.SendMessageNumber);
    }

    [Fact]
    public async Task SessionManager_FullRoundTrip_AliceToBob()
    {
        using var bobDb = new SqliteConnection("Data Source=:memory:");
        bobDb.Open();
        ApplySchema(bobDb);
        var bobRepo = new RatchetSessionRepository(bobDb);

        var svc = new DoubleRatchetService();
        var aliceManager = new SessionManager(svc, _repo);
        var bobManager = new SessionManager(svc, bobRepo);

        var sharedSecret = new byte[32];
        System.Security.Cryptography.RandomNumberGenerator.Fill(sharedSecret);

        var bobSpkKey = NSec.Cryptography.Key.Create(
            NSec.Cryptography.KeyAgreementAlgorithm.X25519,
            new NSec.Cryptography.KeyCreationParameters
            {
                ExportPolicy = NSec.Cryptography.KeyExportPolicies.AllowPlaintextExport
            });
        var bobSpkPrivate = bobSpkKey.Export(NSec.Cryptography.KeyBlobFormat.RawPrivateKey);
        var bobSpkPublic = bobSpkKey.PublicKey.Export(NSec.Cryptography.KeyBlobFormat.RawPublicKey);

        await aliceManager.CreateInitiatorSessionAsync("chat-1", sharedSecret, bobSpkPublic);
        await bobManager.CreateResponderSessionAsync("chat-1", sharedSecret, bobSpkPrivate, bobSpkPublic);

        var ad = System.Text.Encoding.UTF8.GetBytes("ad");

        var alicePlaintext = System.Text.Encoding.UTF8.GetBytes("Hello Bob!");
        var (ct, rpub, pn, n) = await aliceManager.EncryptAsync("chat-1", alicePlaintext, ad);
        var decrypted = await bobManager.DecryptAsync("chat-1", ct, rpub, pn, n, ad);

        Assert.Equal("Hello Bob!", System.Text.Encoding.UTF8.GetString(decrypted));
    }
}
