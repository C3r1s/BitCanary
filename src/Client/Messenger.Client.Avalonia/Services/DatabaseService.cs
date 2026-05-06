// Сервис клиента BitCanary: сеть, кэш, медиа — «DatabaseService».
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using System.Security.Cryptography;

namespace Messenger.Client.Avalonia.Services;

public sealed class DatabaseService(IDataProtectionProvider dpProvider)
{
    private static readonly string DefaultLocalAppData = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Messenger.Client.Avalonia");

    private const string DbKeyFileName = "db-key.bin";
    private const string DbFileName = "messenger.db";
    private const string DbKeyPurpose = "Messenger.Db.v1";

    public static string GetDbPath() => Path.Combine(DefaultLocalAppData, DbFileName);

    public async Task<SqliteConnection> OpenAsync(
        string? localAppDataOverride = null,
        CancellationToken cancellationToken = default)
    {
        var localAppData = localAppDataOverride ?? DefaultLocalAppData;
        Directory.CreateDirectory(localAppData);

        await EnsureDbKeyAsync(localAppData, cancellationToken);

        var dbPath = Path.Combine(localAppData, DbFileName);

        var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync(cancellationToken);

        await EnableWalAsync(conn, cancellationToken);
        await ApplySchemaAsync(conn, cancellationToken);
        await MigrateToV2Async(conn, cancellationToken);
        await MigrateToV3Async(conn, cancellationToken);
        await MigrateToV4Async(conn, cancellationToken);
        await MigrateToV5Async(conn, cancellationToken);

        return conn;
    }

    private async Task EnsureDbKeyAsync(string localAppData, CancellationToken ct)
    {
        var keyFile = Path.Combine(localAppData, DbKeyFileName);
        var protector = dpProvider.CreateProtector(DbKeyPurpose);

        if (File.Exists(keyFile))
        {
            var blob = await File.ReadAllBytesAsync(keyFile, ct);
            var rawKey = protector.Unprotect(blob);  // throws CryptographicException if tampered
            CryptographicOperations.ZeroMemory(rawKey);
            return;
        }

        var key = RandomNumberGenerator.GetBytes(32);
        var protectedBlob = protector.Protect(key);
        CryptographicOperations.ZeroMemory(key);
        await File.WriteAllBytesAsync(keyFile, protectedBlob, ct);
    }

    public static async Task ApplySchemaForTestAsync(SqliteConnection conn, CancellationToken ct = default)
    {
        await ApplySchemaAsync(conn, ct);
        await MigrateToV2Async(conn, ct);
        await MigrateToV3Async(conn, ct);
        await MigrateToV4Async(conn, ct);
        await MigrateToV5Async(conn, ct);
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

        await using var cmd2 = conn.CreateCommand();
        cmd2.CommandText = """
            CREATE TRIGGER IF NOT EXISTS messages_ai
            AFTER INSERT ON messages BEGIN
                INSERT INTO messages_fts(rowid, plaintext_body)
                VALUES (new.rowid, new.plaintext_body);
            END
            """;
        await cmd2.ExecuteNonQueryAsync(ct);

        await using var cmd3 = conn.CreateCommand();
        cmd3.CommandText = """
            CREATE TRIGGER IF NOT EXISTS messages_ad
            AFTER DELETE ON messages BEGIN
                INSERT INTO messages_fts(messages_fts, rowid, plaintext_body)
                VALUES ('delete', old.rowid, old.plaintext_body);
            END
            """;
        await cmd3.ExecuteNonQueryAsync(ct);

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

    private static async Task MigrateToV4Async(SqliteConnection conn, CancellationToken ct)
    {
        await using var checkCmd = conn.CreateCommand();
        checkCmd.CommandText = "SELECT COUNT(*) FROM schema_migrations WHERE version = 4";
        var count = (long)(await checkCmd.ExecuteScalarAsync(ct))!;
        if (count > 0) return;

        await using var cmd1 = conn.CreateCommand();
        cmd1.CommandText = "ALTER TABLE messages ADD COLUMN status INTEGER NOT NULL DEFAULT 0";
        await cmd1.ExecuteNonQueryAsync(ct);

        await using var cmd2 = conn.CreateCommand();
        cmd2.CommandText = "INSERT OR IGNORE INTO schema_migrations(version, applied_at) VALUES (4, datetime('now', 'utc'))";
        await cmd2.ExecuteNonQueryAsync(ct);
    }

    private static async Task MigrateToV5Async(SqliteConnection conn, CancellationToken ct)
    {
        await using var checkCmd = conn.CreateCommand();
        checkCmd.CommandText = "SELECT COUNT(*) FROM schema_migrations WHERE version = 5";
        var count = (long)(await checkCmd.ExecuteScalarAsync(ct))!;
        if (count > 0) return;

        await using var fkOff = conn.CreateCommand();
        fkOff.CommandText = "PRAGMA foreign_keys = OFF";
        await fkOff.ExecuteNonQueryAsync(ct);

        try
        {

        await using var cmd1 = conn.CreateCommand();
        cmd1.CommandText = "DROP TRIGGER IF EXISTS messages_au";
        await cmd1.ExecuteNonQueryAsync(ct);

        await using var cmd2 = conn.CreateCommand();
        cmd2.CommandText = "DROP TRIGGER IF EXISTS messages_ad";
        await cmd2.ExecuteNonQueryAsync(ct);

        await using var cmd3 = conn.CreateCommand();
        cmd3.CommandText = "DROP TRIGGER IF EXISTS messages_ai";
        await cmd3.ExecuteNonQueryAsync(ct);

        await using var cmd4 = conn.CreateCommand();
        cmd4.CommandText = "DROP TABLE IF EXISTS messages_fts";
        await cmd4.ExecuteNonQueryAsync(ct);

        await using var cmdClean1 = conn.CreateCommand();
        cmdClean1.CommandText = "DROP TABLE IF EXISTS chats_new";
        await cmdClean1.ExecuteNonQueryAsync(ct);

        await using var cmdClean2 = conn.CreateCommand();
        cmdClean2.CommandText = "DROP TABLE IF EXISTS messages_new";
        await cmdClean2.ExecuteNonQueryAsync(ct);

        await using var cmd5 = conn.CreateCommand();
        cmd5.CommandText = """
            CREATE TABLE chats_new (
                id                   TEXT NOT NULL,
                owner_user_id        TEXT NOT NULL DEFAULT '',
                name                 TEXT NOT NULL,
                type                 INTEGER NOT NULL,
                last_message_preview TEXT NULL,
                unread_count         INTEGER NOT NULL DEFAULT 0,
                updated_at           TEXT NOT NULL,
                PRIMARY KEY (id, owner_user_id)
            )
            """;
        await cmd5.ExecuteNonQueryAsync(ct);

        await using var cmd6 = conn.CreateCommand();
        cmd6.CommandText = """
            INSERT INTO chats_new (id, owner_user_id, name, type, last_message_preview, unread_count, updated_at)
            SELECT id, '', name, type, last_message_preview, unread_count, updated_at FROM chats
            """;
        await cmd6.ExecuteNonQueryAsync(ct);

        await using var cmd7 = conn.CreateCommand();
        cmd7.CommandText = "DROP TABLE chats";
        await cmd7.ExecuteNonQueryAsync(ct);

        await using var cmd8 = conn.CreateCommand();
        cmd8.CommandText = "ALTER TABLE chats_new RENAME TO chats";
        await cmd8.ExecuteNonQueryAsync(ct);

        await using var cmd9 = conn.CreateCommand();
        cmd9.CommandText = """
            CREATE TABLE messages_new (
                id                   TEXT NOT NULL,
                owner_user_id        TEXT NOT NULL DEFAULT '',
                chat_id              TEXT NOT NULL,
                sender_id            TEXT NOT NULL,
                client_message_id    TEXT NOT NULL,
                protocol_version     INTEGER NOT NULL DEFAULT 0,
                encrypted_payload    TEXT NOT NULL,
                key_envelope         TEXT NOT NULL,
                encryption_algorithm TEXT NOT NULL,
                sent_at              TEXT NOT NULL,
                plaintext_body       TEXT NULL,
                status               INTEGER NOT NULL DEFAULT 0,
                PRIMARY KEY (id, owner_user_id),
                UNIQUE (client_message_id, owner_user_id)
            )
            """;
        await cmd9.ExecuteNonQueryAsync(ct);

        await using var cmd10 = conn.CreateCommand();
        cmd10.CommandText = """
            INSERT INTO messages_new (id, owner_user_id, chat_id, sender_id, client_message_id,
                protocol_version, encrypted_payload, key_envelope, encryption_algorithm,
                sent_at, plaintext_body, status)
            SELECT id, '', chat_id, sender_id, client_message_id,
                protocol_version, encrypted_payload, key_envelope, encryption_algorithm,
                sent_at, plaintext_body, status
            FROM messages
            """;
        await cmd10.ExecuteNonQueryAsync(ct);

        await using var cmd11 = conn.CreateCommand();
        cmd11.CommandText = "DROP TABLE messages";
        await cmd11.ExecuteNonQueryAsync(ct);

        await using var cmd12 = conn.CreateCommand();
        cmd12.CommandText = "ALTER TABLE messages_new RENAME TO messages";
        await cmd12.ExecuteNonQueryAsync(ct);

        await using var cmd13 = conn.CreateCommand();
        cmd13.CommandText = "CREATE INDEX IF NOT EXISTS idx_messages_chat_sent ON messages(chat_id, sent_at)";
        await cmd13.ExecuteNonQueryAsync(ct);

        await using var cmd14 = conn.CreateCommand();
        cmd14.CommandText = """
            CREATE VIRTUAL TABLE IF NOT EXISTS messages_fts USING fts5(
                plaintext_body,
                content='messages',
                content_rowid='rowid',
                tokenize='unicode61 remove_diacritics 1'
            )
            """;
        await cmd14.ExecuteNonQueryAsync(ct);

        await using var cmd15 = conn.CreateCommand();
        cmd15.CommandText = """
            CREATE TRIGGER IF NOT EXISTS messages_ai
            AFTER INSERT ON messages BEGIN
                INSERT INTO messages_fts(rowid, plaintext_body)
                VALUES (new.rowid, new.plaintext_body);
            END
            """;
        await cmd15.ExecuteNonQueryAsync(ct);

        await using var cmd16 = conn.CreateCommand();
        cmd16.CommandText = """
            CREATE TRIGGER IF NOT EXISTS messages_ad
            AFTER DELETE ON messages BEGIN
                INSERT INTO messages_fts(messages_fts, rowid, plaintext_body)
                VALUES ('delete', old.rowid, old.plaintext_body);
            END
            """;
        await cmd16.ExecuteNonQueryAsync(ct);

        await using var cmd17 = conn.CreateCommand();
        cmd17.CommandText = """
            CREATE TRIGGER IF NOT EXISTS messages_au
            AFTER UPDATE OF plaintext_body ON messages BEGIN
                INSERT INTO messages_fts(messages_fts, rowid, plaintext_body)
                VALUES ('delete', old.rowid, old.plaintext_body);
                INSERT INTO messages_fts(rowid, plaintext_body)
                VALUES (new.rowid, new.plaintext_body);
            END
            """;
        await cmd17.ExecuteNonQueryAsync(ct);

        await using var cmd18 = conn.CreateCommand();
        cmd18.CommandText = """
            INSERT INTO messages_fts(rowid, plaintext_body)
            SELECT rowid, plaintext_body FROM messages
            WHERE plaintext_body IS NOT NULL AND plaintext_body != ''
            """;
        await cmd18.ExecuteNonQueryAsync(ct);

        await using var cmd19 = conn.CreateCommand();
        cmd19.CommandText = "INSERT OR IGNORE INTO schema_migrations(version, applied_at) VALUES (5, datetime('now', 'utc'))";
        await cmd19.ExecuteNonQueryAsync(ct);

        } // end try
        finally
        {
            await using var fkOn = conn.CreateCommand();
            fkOn.CommandText = "PRAGMA foreign_keys = ON";
            await fkOn.ExecuteNonQueryAsync(ct);
        }
    }
}
