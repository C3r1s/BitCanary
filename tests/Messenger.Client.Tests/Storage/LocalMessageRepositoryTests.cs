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
        DatabaseService.ApplySchemaForTestAsync(_conn).GetAwaiter().GetResult();
        _repo = new LocalMessageRepository(_conn);
    }

    public async ValueTask DisposeAsync()
    {
        await _conn.DisposeAsync();
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

    // ── Test 6: UpdatePlaintextBodyAsync sets plaintext and is idempotent ─

    [Fact]
    public async Task UpdatePlaintextBodyAsync_SetsPlaintext_OnlyWhenNull()
    {
        var chatId = Guid.NewGuid();
        await _repo.UpsertChatAsync(MakeChatDto(chatId));

        var msg = MakeMessageDto(chatId);
        await _repo.SaveMessageAsync(msg);

        // First call: sets plaintext_body
        await _repo.UpdatePlaintextBodyAsync(msg.Id, "hello world");

        await using var verifyCmd = _conn.CreateCommand();
        verifyCmd.CommandText = "SELECT plaintext_body FROM messages WHERE id = @id";
        verifyCmd.Parameters.AddWithValue("@id", msg.Id.ToString());
        var stored = (string?)(await verifyCmd.ExecuteScalarAsync());
        Assert.Equal("hello world", stored);

        // Second call: idempotent — should NOT overwrite
        await _repo.UpdatePlaintextBodyAsync(msg.Id, "different text");

        await using var verifyCmd2 = _conn.CreateCommand();
        verifyCmd2.CommandText = "SELECT plaintext_body FROM messages WHERE id = @id";
        verifyCmd2.Parameters.AddWithValue("@id", msg.Id.ToString());
        var stored2 = (string?)(await verifyCmd2.ExecuteScalarAsync());
        Assert.Equal("hello world", stored2);
    }

    // ── Test 7: UpdatePlaintextBodyAsync populates FTS index ─────────────

    [Fact]
    public async Task UpdatePlaintextBodyAsync_PopulatesFtsIndex()
    {
        var chatId = Guid.NewGuid();
        await _repo.UpsertChatAsync(MakeChatDto(chatId));

        var msg = MakeMessageDto(chatId);
        await _repo.SaveMessageAsync(msg);

        await _repo.UpdatePlaintextBodyAsync(msg.Id, "searchable content");

        await using var ftsCmd = _conn.CreateCommand();
        ftsCmd.CommandText = "SELECT rowid FROM messages_fts WHERE messages_fts MATCH @term";
        ftsCmd.Parameters.AddWithValue("@term", "searchable");
        var rowid = await ftsCmd.ExecuteScalarAsync();

        Assert.NotNull(rowid);
    }
}
