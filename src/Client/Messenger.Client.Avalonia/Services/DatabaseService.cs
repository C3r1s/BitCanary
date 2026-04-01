using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using System.Security.Cryptography;

namespace Messenger.Client.Avalonia.Services;

/// <summary>
/// Opens and migrates the local SQLite database (WAL mode).
/// Must be called once at startup before any repository access.
/// </summary>
public sealed class DatabaseService(IDataProtectionProvider dpProvider)
{
    private static readonly string DefaultLocalAppData = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Messenger.Client.Avalonia");

    private const string DbKeyFileName = "db-key.bin";
    private const string DbFileName = "messenger.db";
    private const string DbKeyPurpose = "Messenger.Db.v1";

    public static string GetDbPath() => Path.Combine(DefaultLocalAppData, DbFileName);

    /// <summary>
    /// Opens (or creates) the SQLite database with WAL mode and applies the schema.
    /// </summary>
    /// <param name="localAppDataOverride">Override the local app data directory (for testing).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<SqliteConnection> OpenAsync(
        string? localAppDataOverride = null,
        CancellationToken cancellationToken = default)
    {
        var localAppData = localAppDataOverride ?? DefaultLocalAppData;
        Directory.CreateDirectory(localAppData);

        await EnsureDbKeyAsync(localAppData, cancellationToken);

        var dbPath = Path.Combine(localAppData, DbFileName);

        // Do NOT use Cache=Shared — incompatible with WAL mode
        var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync(cancellationToken);

        await EnableWalAsync(conn, cancellationToken);
        await ApplySchemaAsync(conn, cancellationToken);
        await MigrateToV2Async(conn, cancellationToken);
        await MigrateToV3Async(conn, cancellationToken);

        return conn;
    }

    private async Task EnsureDbKeyAsync(string localAppData, CancellationToken ct)
    {
        var keyFile = Path.Combine(localAppData, DbKeyFileName);
        var protector = dpProvider.CreateProtector(DbKeyPurpose);

        if (File.Exists(keyFile))
        {
            // Load and verify key is readable (detect corruption early)
            var blob = await File.ReadAllBytesAsync(keyFile, ct);
            var rawKey = protector.Unprotect(blob);  // throws CryptographicException if tampered
            CryptographicOperations.ZeroMemory(rawKey);
            return;
        }

        // Generate and protect new DB key
        var key = RandomNumberGenerator.GetBytes(32);
        var protectedBlob = protector.Protect(key);
        CryptographicOperations.ZeroMemory(key);
        await File.WriteAllBytesAsync(keyFile, protectedBlob, ct);
    }

    /// <summary>
    /// Applies the full schema to the provided connection — exposed for unit/integration tests
    /// that open an in-memory SQLite database directly without going through <see cref="OpenAsync"/>.
    /// </summary>
    public static async Task ApplySchemaForTestAsync(SqliteConnection conn, CancellationToken ct = default)
    {
        await ApplySchemaAsync(conn, ct);
        await MigrateToV2Async(conn, ct);
        await MigrateToV3Async(conn, ct);
    }

    private static async Task EnableWalAsync(SqliteConnection conn, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA journal_mode = WAL;";
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task ApplySchemaAsync(SqliteConnection conn, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS chats (
                id                   TEXT PRIMARY KEY NOT NULL,
                name                 TEXT NOT NULL,
                type                 INTEGER NOT NULL,
                last_message_preview TEXT NULL,
                unread_count         INTEGER NOT NULL DEFAULT 0,
                updated_at           TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS messages (
                id                   TEXT PRIMARY KEY NOT NULL,
                chat_id              TEXT NOT NULL REFERENCES chats(id),
                sender_id            TEXT NOT NULL,
                client_message_id    TEXT NOT NULL UNIQUE,
                protocol_version     INTEGER NOT NULL DEFAULT 0,
                encrypted_payload    TEXT NOT NULL,
                key_envelope         TEXT NOT NULL,
                encryption_algorithm TEXT NOT NULL,
                sent_at              TEXT NOT NULL,
                plaintext_body       TEXT NULL
            );

            CREATE INDEX IF NOT EXISTS idx_messages_chat_sent
                ON messages(chat_id, sent_at);

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

            CREATE TABLE IF NOT EXISTS schema_migrations (
                version    INTEGER PRIMARY KEY,
                applied_at TEXT NOT NULL
            );

            INSERT OR IGNORE INTO schema_migrations(version, applied_at)
            VALUES (1, datetime('now', 'utc'));
            """;
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task MigrateToV2Async(SqliteConnection conn, CancellationToken ct)
    {
        await using var checkCmd = conn.CreateCommand();
        checkCmd.CommandText = "SELECT COUNT(*) FROM schema_migrations WHERE version = 2";
        var count = (long)(await checkCmd.ExecuteScalarAsync(ct))!;
        if (count > 0) return;

        // SQLite requires separate commands per ALTER TABLE (not batched)
        await using var cmd1 = conn.CreateCommand();
        cmd1.CommandText = "ALTER TABLE ratchet_sessions ADD COLUMN verified INTEGER NOT NULL DEFAULT 0";
        await cmd1.ExecuteNonQueryAsync(ct);

        await using var cmd2 = conn.CreateCommand();
        cmd2.CommandText = "ALTER TABLE ratchet_sessions ADD COLUMN last_verified_at TEXT NULL";
        await cmd2.ExecuteNonQueryAsync(ct);

        await using var cmd3 = conn.CreateCommand();
        cmd3.CommandText = "ALTER TABLE ratchet_sessions ADD COLUMN remote_ik_public BLOB NULL";
        await cmd3.ExecuteNonQueryAsync(ct);

        await using var cmd4 = conn.CreateCommand();
        cmd4.CommandText = "INSERT OR IGNORE INTO schema_migrations(version, applied_at) VALUES (2, datetime('now', 'utc'))";
        await cmd4.ExecuteNonQueryAsync(ct);
    }

    private static async Task MigrateToV3Async(SqliteConnection conn, CancellationToken ct)
    {
        await using var checkCmd = conn.CreateCommand();
        checkCmd.CommandText = "SELECT COUNT(*) FROM schema_migrations WHERE version = 3";
        var count = (long)(await checkCmd.ExecuteScalarAsync(ct))!;
        if (count > 0) return;

        // FTS5 virtual table for full-text search over decrypted message bodies.
        // content_rowid='rowid' uses the implicit integer rowid (NOT the UUID id column) — Pitfall 1.
        await using var cmd1 = conn.CreateCommand();
        cmd1.CommandText = """
            CREATE VIRTUAL TABLE IF NOT EXISTS messages_fts USING fts5(
                plaintext_body,
                content='messages',
                content_rowid='rowid',
                tokenize='unicode61 remove_diacritics 1'
            )
            """;
        await cmd1.ExecuteNonQueryAsync(ct);

        // AFTER INSERT trigger — index new plaintext rows
        await using var cmd2 = conn.CreateCommand();
        cmd2.CommandText = """
            CREATE TRIGGER IF NOT EXISTS messages_ai
            AFTER INSERT ON messages BEGIN
                INSERT INTO messages_fts(rowid, plaintext_body)
                VALUES (new.rowid, new.plaintext_body);
            END
            """;
        await cmd2.ExecuteNonQueryAsync(ct);

        // AFTER DELETE trigger — remove deleted rows from FTS index
        await using var cmd3 = conn.CreateCommand();
        cmd3.CommandText = """
            CREATE TRIGGER IF NOT EXISTS messages_ad
            AFTER DELETE ON messages BEGIN
                INSERT INTO messages_fts(messages_fts, rowid, plaintext_body)
                VALUES ('delete', old.rowid, old.plaintext_body);
            END
            """;
        await cmd3.ExecuteNonQueryAsync(ct);

        // AFTER UPDATE trigger — update FTS index when plaintext_body changes
        await using var cmd4 = conn.CreateCommand();
        cmd4.CommandText = """
            CREATE TRIGGER IF NOT EXISTS messages_au
            AFTER UPDATE OF plaintext_body ON messages BEGIN
                INSERT INTO messages_fts(messages_fts, rowid, plaintext_body)
                VALUES ('delete', old.rowid, old.plaintext_body);
                INSERT INTO messages_fts(rowid, plaintext_body)
                VALUES (new.rowid, new.plaintext_body);
            END
            """;
        await cmd4.ExecuteNonQueryAsync(ct);

        // Backfill existing plaintext rows into FTS index
        await using var cmd5 = conn.CreateCommand();
        cmd5.CommandText = """
            INSERT INTO messages_fts(rowid, plaintext_body)
            SELECT rowid, plaintext_body FROM messages
            WHERE plaintext_body IS NOT NULL AND plaintext_body != ''
            """;
        await cmd5.ExecuteNonQueryAsync(ct);

        await using var cmd6 = conn.CreateCommand();
        cmd6.CommandText = "INSERT OR IGNORE INTO schema_migrations(version, applied_at) VALUES (3, datetime('now', 'utc'))";
        await cmd6.ExecuteNonQueryAsync(ct);
    }
}
