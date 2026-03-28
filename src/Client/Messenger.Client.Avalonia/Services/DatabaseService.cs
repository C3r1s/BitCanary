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
    private static readonly string LocalAppData = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Messenger.Client.Avalonia");

    private const string DbKeyFileName = "db-key.bin";
    private const string DbFileName = "messenger.db";
    private const string DbKeyPurpose = "Messenger.Db.v1";

    public static string GetDbPath() => Path.Combine(LocalAppData, DbFileName);

    public async Task<SqliteConnection> OpenAsync(string? dbPathOverride = null, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(LocalAppData);
        await EnsureDbKeyAsync(cancellationToken);

        var dbPath = dbPathOverride ?? GetDbPath();

        // Do NOT use Cache=Shared — incompatible with WAL mode
        var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync(cancellationToken);

        await EnableWalAsync(conn, cancellationToken);
        await ApplySchemaAsync(conn, cancellationToken);

        return conn;
    }

    private async Task EnsureDbKeyAsync(CancellationToken ct)
    {
        var keyFile = Path.Combine(LocalAppData, DbKeyFileName);
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
}
