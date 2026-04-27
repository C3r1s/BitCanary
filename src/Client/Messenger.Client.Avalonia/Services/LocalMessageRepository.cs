using Messenger.Shared.Contracts;
using Messenger.Shared.Contracts.Dtos;
using Microsoft.Data.Sqlite;

namespace Messenger.Client.Avalonia.Services;

/// <summary>
/// SQLite-backed implementation of <see cref="ILocalMessageRepository"/>.
/// All reads and writes are scoped to <see cref="IClientSessionService.CurrentUserId"/>
/// via the <c>owner_user_id</c> column (added in schema V5).
/// </summary>
public sealed class LocalMessageRepository(
    SqliteConnection connection,
    IClientSessionService sessionService) : ILocalMessageRepository
{
    private string OwnerId => sessionService.CurrentUserId.ToString();

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
                (id, owner_user_id, chat_id, sender_id, client_message_id, protocol_version,
                 encrypted_payload, key_envelope, encryption_algorithm, sent_at)
            VALUES
                (@id, @ownerId, @chatId, @senderId, @clientMessageId, @protocolVersion,
                 @encryptedPayload, @keyEnvelope, @encryptionAlgorithm, @sentAt)
            """;
        cmd.Parameters.AddWithValue("@id", message.Id.ToString());
        cmd.Parameters.AddWithValue("@ownerId", OwnerId);
        cmd.Parameters.AddWithValue("@chatId", message.ChatId.ToString());
        cmd.Parameters.AddWithValue("@senderId", message.SenderId.ToString());
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

    /// <inheritdoc/>
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

    // ── Chats ─────────────────────────────────────────────────────────────

    /// <inheritdoc/>
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

    /// <inheritdoc/>
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

    /// <inheritdoc/>
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

    /// <inheritdoc/>
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

    /// <inheritdoc/>
    public async Task DeleteChatAsync(Guid chatId, CancellationToken ct = default)
    {
        // Delete messages first — FK constraint requires this order
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

    /// <inheritdoc/>
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

    /// <inheritdoc/>
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
