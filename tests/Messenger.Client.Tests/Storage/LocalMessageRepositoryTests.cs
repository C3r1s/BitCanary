using Messenger.Client.Avalonia.Services;
using Messenger.Shared.Contracts;
using Messenger.Shared.Contracts.Dtos;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Messenger.Client.Tests.Storage;

[Trait("Category", "Unit")]
public sealed class LocalMessageRepositoryTests : IAsyncDisposable
{
    private readonly SqliteConnection _conn;
    private readonly LocalMessageRepository _repo;

    public LocalMessageRepositoryTests()
    {
        _conn = new SqliteConnection("Data Source=:memory:");
        _conn.Open();
        ApplySchema(_conn);
        _repo = new LocalMessageRepository(_conn);
    }

    public async ValueTask DisposeAsync()
    {
        await _conn.DisposeAsync();
    }

    private static void ApplySchema(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
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
            """;
        cmd.ExecuteNonQuery();
    }

    private static ChatSummaryDto MakeChatDto(Guid? id = null) => new(
        Id: id ?? Guid.NewGuid(),
        Title: "Test Chat",
        Type: ChatType.Direct,
        AvatarUrl: null,
        Description: null,
        LastMessage: null,
        UnreadCount: 0,
        Members: Array.Empty<ChatMemberDto>());

    private static MessageDto MakeMessageDto(Guid chatId, Guid? id = null) => new(
        Id: id ?? Guid.NewGuid(),
        ChatId: chatId,
        SenderId: Guid.NewGuid(),
        SenderDisplayName: "Alice",
        Kind: MessageKind.Text,
        EncryptedPayload: "enc-data",
        EncryptionAlgorithm: "AES-GCM",
        KeyEnvelope: "key-env",
        MediaId: null,
        ReplyToMessageId: null,
        MetadataJson: null,
        CreatedAtUtc: DateTimeOffset.UtcNow);

    // ── Test 1: Save and retrieve a message ──────────────────────────────

    [Fact]
    public async Task SaveAndRetrieveMessage_ReturnsStoredMessage()
    {
        var chatId = Guid.NewGuid();
        var chat = MakeChatDto(chatId);
        await _repo.UpsertChatAsync(chat);

        var msg = MakeMessageDto(chatId);
        await _repo.SaveMessageAsync(msg, protocolVersion: 0);

        var results = await _repo.GetMessagesAsync(chatId);

        Assert.Single(results);
        Assert.Equal(msg.Id, results[0].Id);
    }

    // ── Test 2: MessageExistsAsync returns true for saved message ────────

    [Fact]
    public async Task MessageExistsAsync_ReturnsTrueForSavedMessage()
    {
        var chatId = Guid.NewGuid();
        var chat = MakeChatDto(chatId);
        await _repo.UpsertChatAsync(chat);

        var msg = MakeMessageDto(chatId);
        await _repo.SaveMessageAsync(msg);

        var exists = await _repo.MessageExistsAsync(msg.Id);

        Assert.True(exists);
    }

    // ── Test 3: MessageExistsAsync returns false for unknown id ──────────

    [Fact]
    public async Task MessageExistsAsync_ReturnsFalseForUnknownId()
    {
        var exists = await _repo.MessageExistsAsync(Guid.NewGuid());

        Assert.False(exists);
    }

    // ── Test 4: Upsert and get chats ─────────────────────────────────────

    [Fact]
    public async Task UpsertAndGetChats_ReturnsStoredChat()
    {
        var chat = MakeChatDto();
        await _repo.UpsertChatAsync(chat);

        var results = await _repo.GetChatsAsync();

        Assert.Single(results);
        Assert.Equal(chat.Title, results[0].Title);
    }

    // ── Test 5: Save same message twice is idempotent ────────────────────

    [Fact]
    public async Task SaveMessage_IsIdempotent_NoDuplicates()
    {
        var chatId = Guid.NewGuid();
        var chat = MakeChatDto(chatId);
        await _repo.UpsertChatAsync(chat);

        var msg = MakeMessageDto(chatId);
        await _repo.SaveMessageAsync(msg, protocolVersion: 0);
        await _repo.SaveMessageAsync(msg, protocolVersion: 0);  // same message

        var results = await _repo.GetMessagesAsync(chatId);

        Assert.Single(results);
    }
}
