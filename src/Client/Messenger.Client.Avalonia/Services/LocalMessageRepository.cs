using Messenger.Shared.Contracts;
using Messenger.Shared.Contracts.Dtos;
using Microsoft.Data.Sqlite;

namespace Messenger.Client.Avalonia.Services;

/// <summary>
/// SQLite-backed implementation of <see cref="ILocalMessageRepository"/>.
/// Receives an open <see cref="SqliteConnection"/> (schema already applied by <see cref="DatabaseService"/>).
/// </summary>
public sealed class LocalMessageRepository(SqliteConnection connection) : ILocalMessageRepository
{
    // ── Messages ──────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task SaveMessageAsync(
        MessageDto message,
        int protocolVersion = 0,
        CancellationToken cancellationToken = default)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT OR IGNORE INTO messages
                (id, chat_id, sender_id, client_message_id, protocol_version,
                 encrypted_payload, key_envelope, encryption_algorithm, sent_at)
            VALUES
                (@id, @chatId, @senderId, @clientMessageId, @protocolVersion,
                 @encryptedPayload, @keyEnvelope, @encryptionAlgorithm, @sentAt)
            """;
        cmd.Parameters.AddWithValue("@id", message.Id.ToString());
        cmd.Parameters.AddWithValue("@chatId", message.ChatId.ToString());
        cmd.Parameters.AddWithValue("@senderId", message.SenderId.ToString());
        // MessageDto does not have a separate ClientMessageId; use Id for dedup
        cmd.Parameters.AddWithValue("@clientMessageId", message.Id.ToString());
        cmd.Parameters.AddWithValue("@protocolVersion", protocolVersion);
        cmd.Parameters.AddWithValue("@encryptedPayload", message.EncryptedPayload);
        cmd.Parameters.AddWithValue("@keyEnvelope", message.KeyEnvelope);
        cmd.Parameters.AddWithValue("@encryptionAlgorithm", message.EncryptionAlgorithm);
        cmd.Parameters.AddWithValue("@sentAt", message.CreatedAtUtc.ToString("O"));
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<MessageDto>> GetMessagesAsync(
        Guid chatId,
        CancellationToken cancellationToken = default)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, chat_id, sender_id,
                   encrypted_payload, key_envelope, encryption_algorithm, sent_at
            FROM messages
            WHERE chat_id = @chatId
            ORDER BY sent_at ASC
            """;
        cmd.Parameters.AddWithValue("@chatId", chatId.ToString());

        var results = new List<MessageDto>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new MessageDto(
                Id: Guid.Parse(reader.GetString(0)),
                ChatId: Guid.Parse(reader.GetString(1)),
                SenderId: Guid.Parse(reader.GetString(2)),
                SenderDisplayName: string.Empty,   // not stored; populated from server on next sync
                Kind: MessageKind.Text,             // not stored; default for legacy messages
                EncryptedPayload: reader.GetString(3),
                EncryptionAlgorithm: reader.GetString(5),
                KeyEnvelope: reader.GetString(4),
                MediaId: null,
                ReplyToMessageId: null,
                MetadataJson: null,
                CreatedAtUtc: DateTimeOffset.Parse(
                    reader.GetString(6),
                    null,
                    System.Globalization.DateTimeStyles.RoundtripKind)));
        }
        return results;
    }

    /// <inheritdoc/>
    public async Task<bool> MessageExistsAsync(
        Guid clientMessageId,
        CancellationToken cancellationToken = default)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText =
            "SELECT COUNT(*) FROM messages WHERE client_message_id = @id";
        cmd.Parameters.AddWithValue("@id", clientMessageId.ToString());
        var count = (long)(await cmd.ExecuteScalarAsync(cancellationToken))!;
        return count > 0;
    }

    /// <inheritdoc/>
    public async Task UpdatePlaintextBodyAsync(
        Guid messageId,
        string plaintextBody,
        CancellationToken ct = default)
    {
        await using var cmd = connection.CreateCommand();
        // Guard: AND (plaintext_body IS NULL OR plaintext_body = '') prevents redundant FTS trigger
        // churn on already-indexed rows (idempotency requirement from SRCH-01).
        cmd.CommandText = """
            UPDATE messages
            SET plaintext_body = @plaintext
            WHERE id = @id
              AND (plaintext_body IS NULL OR plaintext_body = '')
            """;
        cmd.Parameters.AddWithValue("@plaintext", plaintextBody);
        cmd.Parameters.AddWithValue("@id", messageId.ToString());
        await cmd.ExecuteNonQueryAsync(ct);
    }

    // ── Chats ─────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task UpsertChatAsync(
        ChatSummaryDto chat,
        CancellationToken cancellationToken = default)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT OR REPLACE INTO chats
                (id, name, type, last_message_preview, unread_count, updated_at)
            VALUES
                (@id, @name, @type, @lastMessagePreview, @unreadCount, @updatedAt)
            """;
        cmd.Parameters.AddWithValue("@id", chat.Id.ToString());
        cmd.Parameters.AddWithValue("@name", chat.Title);
        cmd.Parameters.AddWithValue("@type", (int)chat.Type);
        cmd.Parameters.AddWithValue(
            "@lastMessagePreview",
            (object?)chat.LastMessage?.EncryptedPayload ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@unreadCount", chat.UnreadCount);
        // ChatSummaryDto has no UpdatedAtUtc; use LastMessage time or current UTC
        var updatedAt = chat.LastMessage?.CreatedAtUtc ?? DateTimeOffset.UtcNow;
        cmd.Parameters.AddWithValue("@updatedAt", updatedAt.ToString("O"));
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ChatSummaryDto>> GetChatsAsync(
        CancellationToken cancellationToken = default)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, name, type, last_message_preview, unread_count, updated_at
            FROM chats
            ORDER BY updated_at DESC
            """;

        var results = new List<ChatSummaryDto>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new ChatSummaryDto(
                Id: Guid.Parse(reader.GetString(0)),
                Title: reader.GetString(1),
                Type: (ChatType)reader.GetInt32(2),
                AvatarUrl: null,
                Description: null,
                LastMessage: null,          // preview text stored separately; full message loaded on demand
                UnreadCount: reader.GetInt32(4),
                Members: Array.Empty<ChatMemberDto>()));
        }
        return results;
    }
}
