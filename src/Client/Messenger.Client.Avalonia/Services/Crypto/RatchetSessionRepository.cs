using Microsoft.Data.Sqlite;

namespace Messenger.Client.Avalonia.Services.Crypto;

/// <summary>
/// SQLite-backed implementation of <see cref="IRatchetSessionRepository"/>.
/// Receives an open <see cref="SqliteConnection"/> (schema already applied by DatabaseService).
/// </summary>
public sealed class RatchetSessionRepository(SqliteConnection connection) : IRatchetSessionRepository
{
    /// <inheritdoc/>
    public async Task<byte[]?> LoadSessionAsync(string sessionId, CancellationToken ct = default)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT session_blob FROM ratchet_sessions WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", sessionId);
        var result = await cmd.ExecuteScalarAsync(ct);
        if (result is null || result == DBNull.Value)
            return null;
        return (byte[])result;
    }

    /// <inheritdoc/>
    public async Task SaveSessionAsync(string sessionId, byte[] blob, CancellationToken ct = default)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT OR REPLACE INTO ratchet_sessions(id, session_blob, updated_at)
            VALUES ($id, $blob, datetime('now', 'utc'))
            """;
        cmd.Parameters.AddWithValue("$id", sessionId);
        cmd.Parameters.AddWithValue("$blob", blob);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <inheritdoc/>
    public async Task<byte[]?> TryConsumeSkippedKeyAsync(
        string sessionId,
        byte[] ratchetPubkey,
        int messageNumber,
        CancellationToken ct = default)
    {
        await using var tx = await connection.BeginTransactionAsync(ct);
        try
        {
            byte[]? key;

            await using (var selectCmd = connection.CreateCommand())
            {
                selectCmd.Transaction = (SqliteTransaction)tx;
                selectCmd.CommandText = """
                    SELECT message_key FROM skipped_message_keys
                    WHERE session_id = $sid AND ratchet_pubkey = $rpk AND message_number = $mn
                    """;
                selectCmd.Parameters.AddWithValue("$sid", sessionId);
                selectCmd.Parameters.AddWithValue("$rpk", ratchetPubkey);
                selectCmd.Parameters.AddWithValue("$mn", messageNumber);
                var result = await selectCmd.ExecuteScalarAsync(ct);
                if (result is null || result == DBNull.Value)
                {
                    await tx.RollbackAsync(ct);
                    return null;
                }
                key = (byte[])result;
            }

            await using (var deleteCmd = connection.CreateCommand())
            {
                deleteCmd.Transaction = (SqliteTransaction)tx;
                deleteCmd.CommandText = """
                    DELETE FROM skipped_message_keys
                    WHERE session_id = $sid AND ratchet_pubkey = $rpk AND message_number = $mn
                    """;
                deleteCmd.Parameters.AddWithValue("$sid", sessionId);
                deleteCmd.Parameters.AddWithValue("$rpk", ratchetPubkey);
                deleteCmd.Parameters.AddWithValue("$mn", messageNumber);
                await deleteCmd.ExecuteNonQueryAsync(ct);
            }

            await tx.CommitAsync(ct);
            return key;
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task SaveSkippedKeyAsync(
        string sessionId,
        byte[] ratchetPubkey,
        int messageNumber,
        byte[] messageKey,
        CancellationToken ct = default)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT OR REPLACE INTO skipped_message_keys
                (session_id, ratchet_pubkey, message_number, message_key, stored_at)
            VALUES ($sid, $rpk, $mn, $mk, datetime('now', 'utc'))
            """;
        cmd.Parameters.AddWithValue("$sid", sessionId);
        cmd.Parameters.AddWithValue("$rpk", ratchetPubkey);
        cmd.Parameters.AddWithValue("$mn", messageNumber);
        cmd.Parameters.AddWithValue("$mk", messageKey);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <inheritdoc/>
    public async Task EnforceSkippedKeyLimitAsync(string sessionId, int maxKeys, CancellationToken ct = default)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            DELETE FROM skipped_message_keys
            WHERE session_id = $sid
              AND rowid NOT IN (
                  SELECT rowid FROM skipped_message_keys
                  WHERE session_id = $sid
                  ORDER BY stored_at DESC
                  LIMIT $max
              )
            """;
        cmd.Parameters.AddWithValue("$sid", sessionId);
        cmd.Parameters.AddWithValue("$max", maxKeys);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
