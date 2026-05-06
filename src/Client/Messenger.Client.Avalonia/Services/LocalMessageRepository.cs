// Сервис клиента BitCanary: сеть, кэш, медиа — «LocalMessageRepository».
using Messenger.Shared.Contracts;
using Messenger.Shared.Contracts.Dtos;
using Microsoft.Data.Sqlite;

namespace Messenger.Client.Avalonia.Services;

public sealed class LocalMessageRepository(
    SqliteConnection connection,
    IClientSessionService sessionService) : ILocalMessageRepository
{
    private string OwnerId => sessionService.CurrentUserId.ToString();

    private static async Task InsertMessageRowAsync(
        SqliteConnection conn,
        string ownerId,
        MessageDto message,
        int protocolVersion,
        SqliteTransaction? transaction,
        CancellationToken cancellationToken)
    {
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = """
            INSERT OR IGNORE INTO messages
                (id, owner_user_id, chat_id, sender_id, client_message_id, protocol_version,
                 encrypted_payload, key_envelope, encryption_algorithm, sent_at, status)
            VALUES
                (@id, @ownerId, @chatId, @senderId, @clientMessageId, @protocolVersion,
                 @encryptedPayload, @keyEnvelope, @encryptionAlgorithm, @sentAt, @status)
            """;
        cmd.Parameters.AddWithValue("@id", message.Id.ToString());
        cmd.Parameters.AddWithValue("@ownerId", ownerId);
        cmd.Parameters.AddWithValue("@chatId", message.ChatId.ToString());
        cmd.Parameters.AddWithValue("@senderId", message.SenderId.ToString());
        cmd.Parameters.AddWithValue("@clientMessageId", message.Id.ToString());
        cmd.Parameters.AddWithValue("@protocolVersion", protocolVersion);
        cmd.Parameters.AddWithValue("@encryptedPayload", message.EncryptedPayload);
        cmd.Parameters.AddWithValue("@keyEnvelope", message.KeyEnvelope);
        cmd.Parameters.AddWithValue("@encryptionAlgorithm", message.EncryptionAlgorithm);
        cmd.Parameters.AddWithValue("@sentAt", message.CreatedAtUtc.ToString("O"));
        cmd.Parameters.AddWithValue("@status", (int)message.Status);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SaveMessageAsync(
        MessageDto message,
        int protocolVersion = 0,
        CancellationToken cancellationToken = default) =>
        await InsertMessageRowAsync(connection, OwnerId, message, protocolVersion, null, cancellationToken);

    public async Task ReplaceChatMessagesAsync(
        Guid chatId,
        IReadOnlyList<MessageDto> messages,
        CancellationToken cancellationToken = default)
    {
        var preservedPlaintext = new Dictionary<string, string>(StringComparer.Ordinal);
        await using (var sel = connection.CreateCommand())
        {
            sel.CommandText = """
                SELECT id, plaintext_body FROM messages
                WHERE chat_id = @chatId AND owner_user_id = @ownerId
                  AND plaintext_body IS NOT NULL AND LENGTH(TRIM(plaintext_body)) > 0
                """;
            sel.Parameters.AddWithValue("@chatId", chatId.ToString());
            sel.Parameters.AddWithValue("@ownerId", OwnerId);
            await using var reader = await sel.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                preservedPlaintext[reader.GetString(0)] = reader.GetString(1);
        }

        var serverIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var m in messages)
            serverIds.Add(m.Id.ToString());

        await using var tx = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            await using (var del = connection.CreateCommand())
            {
                del.Transaction = tx;
                del.CommandText = """
                    DELETE FROM messages
                    WHERE chat_id = @chatId AND owner_user_id = @ownerId
                    """;
                del.Parameters.AddWithValue("@chatId", chatId.ToString());
                del.Parameters.AddWithValue("@ownerId", OwnerId);
                await del.ExecuteNonQueryAsync(cancellationToken);
            }

            foreach (var message in messages)
            {
                await InsertMessageRowAsync(
                    connection,
                    OwnerId,
                    message,
                    (int)message.ProtocolVersion,
                    tx,
                    cancellationToken);
            }

            foreach (var (messageId, plaintext) in preservedPlaintext)
            {
                if (!serverIds.Contains(messageId)) continue;

                await using (var up = connection.CreateCommand())
                {
                    up.Transaction = tx;
                    up.CommandText = """
                        UPDATE messages SET plaintext_body = @plaintext
                        WHERE id = @id AND owner_user_id = @ownerId
                        """;
                    up.Parameters.AddWithValue("@plaintext", plaintext);
                    up.Parameters.AddWithValue("@id", messageId);
                    up.Parameters.AddWithValue("@ownerId", OwnerId);
                    await up.ExecuteNonQueryAsync(cancellationToken);
                }
            }

            await tx.CommitAsync(cancellationToken);
        }
        catch
        {
            await tx.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<IReadOnlyList<MessageDto>> GetMessagesAsync(
        Guid chatId,
        CancellationToken cancellationToken = default)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, chat_id, sender_id,
                   encrypted_payload, key_envelope, encryption_algorithm, sent_at, status, protocol_version
            FROM messages
            WHERE chat_id = @chatId
              AND owner_user_id = @ownerId
            ORDER BY sent_at ASC
            """;
        cmd.Parameters.AddWithValue("@chatId", chatId.ToString());
        cmd.Parameters.AddWithValue("@ownerId", OwnerId);

        var results = new List<MessageDto>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new MessageDto(
                Id: Guid.Parse(reader.GetString(0)),
                ChatId: Guid.Parse(reader.GetString(1)),
                SenderId: Guid.Parse(reader.GetString(2)),
                SenderDisplayName: string.Empty,
                Kind: MessageKind.Text,
                EncryptedPayload: reader.GetString(3),
                EncryptionAlgorithm: reader.GetString(5),
                KeyEnvelope: reader.GetString(4),
                MediaId: null,
                ReplyToMessageId: null,
                MetadataJson: null,
                CreatedAtUtc: DateTimeOffset.Parse(
                    reader.GetString(6),
                    null,
                    System.Globalization.DateTimeStyles.RoundtripKind),
                ProtocolVersion: (ProtocolVersion)reader.GetInt32(8),
                Status: (MessageStatus)reader.GetInt32(7)));
        }
        return results;
    }

    public async Task<bool> MessageExistsAsync(
        Guid clientMessageId,
        CancellationToken cancellationToken = default)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT COUNT(*) FROM messages
            WHERE client_message_id = @id
              AND owner_user_id = @ownerId
            """;
        cmd.Parameters.AddWithValue("@id", clientMessageId.ToString());
        cmd.Parameters.AddWithValue("@ownerId", OwnerId);
        var count = (long)(await cmd.ExecuteScalarAsync(cancellationToken))!;
        return count > 0;
    }

    public async Task UpdatePlaintextBodyAsync(
        Guid messageId,
        string plaintextBody,
        CancellationToken ct = default)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            UPDATE messages
            SET plaintext_body = @plaintext
            WHERE id = @id
              AND owner_user_id = @ownerId
              AND (plaintext_body IS NULL OR plaintext_body = '')
            """;
        cmd.Parameters.AddWithValue("@plaintext", plaintextBody);
        cmd.Parameters.AddWithValue("@id", messageId.ToString());
        cmd.Parameters.AddWithValue("@ownerId", OwnerId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<string?> GetPlaintextBodyAsync(
        Guid messageId,
        CancellationToken ct = default)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT plaintext_body
            FROM messages
            WHERE id = @id
              AND owner_user_id = @ownerId
            LIMIT 1
            """;
        cmd.Parameters.AddWithValue("@id", messageId.ToString());
        cmd.Parameters.AddWithValue("@ownerId", OwnerId);

        var result = await cmd.ExecuteScalarAsync(ct);
        if (result is null || result is DBNull) return null;
        var text = result as string;
        return string.IsNullOrEmpty(text) ? null : text;
    }


    public async Task UpsertChatAsync(
        ChatSummaryDto chat,
        CancellationToken cancellationToken = default)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT OR REPLACE INTO chats
                (id, owner_user_id, name, type, last_message_preview, unread_count, updated_at)
            VALUES
                (@id, @ownerId, @name, @type, @lastMessagePreview, @unreadCount, @updatedAt)
            """;
        cmd.Parameters.AddWithValue("@id", chat.Id.ToString());
        cmd.Parameters.AddWithValue("@ownerId", OwnerId);
        cmd.Parameters.AddWithValue("@name", chat.Title);
        cmd.Parameters.AddWithValue("@type", (int)chat.Type);
        cmd.Parameters.AddWithValue(
            "@lastMessagePreview",
            (object?)chat.LastMessage?.EncryptedPayload ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@unreadCount", chat.UnreadCount);
        var updatedAt = chat.LastMessage?.CreatedAtUtc ?? DateTimeOffset.UtcNow;
        cmd.Parameters.AddWithValue("@updatedAt", updatedAt.ToString("O"));
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task ResetUnreadCountAsync(
        Guid chatId,
        CancellationToken cancellationToken = default)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            UPDATE chats SET unread_count = 0
            WHERE id = @chatId
              AND owner_user_id = @ownerId
            """;
        cmd.Parameters.AddWithValue("@chatId", chatId.ToString());
        cmd.Parameters.AddWithValue("@ownerId", OwnerId);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UpdateMessageStatusAsync(
        Guid messageId,
        MessageStatus status,
        CancellationToken cancellationToken = default)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            UPDATE messages
            SET status = @status
            WHERE client_message_id = @messageId
              AND owner_user_id = @ownerId
            """;
        cmd.Parameters.AddWithValue("@status", (int)status);
        cmd.Parameters.AddWithValue("@messageId", messageId.ToString());
        cmd.Parameters.AddWithValue("@ownerId", OwnerId);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task MarkMessagesReadAsync(
        Guid chatId,
        Guid currentUserId,
        CancellationToken cancellationToken = default)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            UPDATE messages
            SET status = @readStatus
            WHERE chat_id = @chatId
              AND sender_id = @senderId
              AND owner_user_id = @ownerId
              AND status < @readStatus
            """;
        cmd.Parameters.AddWithValue("@readStatus", (int)MessageStatus.Read);
        cmd.Parameters.AddWithValue("@chatId", chatId.ToString());
        cmd.Parameters.AddWithValue("@senderId", currentUserId.ToString());
        cmd.Parameters.AddWithValue("@ownerId", OwnerId);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DeleteChatAsync(Guid chatId, CancellationToken ct = default)
    {
        await using var cmd1 = connection.CreateCommand();
        cmd1.CommandText = """
            DELETE FROM messages
            WHERE chat_id = @chatId
              AND owner_user_id = @ownerId
            """;
        cmd1.Parameters.AddWithValue("@chatId", chatId.ToString());
        cmd1.Parameters.AddWithValue("@ownerId", OwnerId);
        await cmd1.ExecuteNonQueryAsync(ct);

        await using var cmd2 = connection.CreateCommand();
        cmd2.CommandText = """
            DELETE FROM chats
            WHERE id = @chatId
              AND owner_user_id = @ownerId
            """;
        cmd2.Parameters.AddWithValue("@chatId", chatId.ToString());
        cmd2.Parameters.AddWithValue("@ownerId", OwnerId);
        await cmd2.ExecuteNonQueryAsync(ct);
    }

    public async Task ClearMessagesAsync(Guid chatId, CancellationToken ct = default)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            DELETE FROM messages
            WHERE chat_id = @chatId
              AND owner_user_id = @ownerId
            """;
        cmd.Parameters.AddWithValue("@chatId", chatId.ToString());
        cmd.Parameters.AddWithValue("@ownerId", OwnerId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<ChatSummaryDto>> GetChatsAsync(
        CancellationToken cancellationToken = default)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, name, type, last_message_preview, unread_count, updated_at
            FROM chats
            WHERE owner_user_id = @ownerId
            ORDER BY updated_at DESC
            """;
        cmd.Parameters.AddWithValue("@ownerId", OwnerId);

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
                LastMessage: null,
                UnreadCount: reader.GetInt32(4),
                Members: Array.Empty<ChatMemberDto>()));
        }
        return results;
    }
}
