// Автотест BitCanary: проверка «LocalSearchServiceTests».
using Messenger.Client.Avalonia.Services;
using Messenger.Shared.Contracts;
using Messenger.Shared.Contracts.Dtos;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Messenger.Client.Tests.Storage;

file sealed class StubSessionServiceForSearch : IClientSessionService
{
    public string ApiBaseUrl => string.Empty;
    public string? AccessToken => null;
    public Guid CurrentUserId { get; }
    public string UserName => "test";
    public bool IsAuthenticated => true;
    public void SetSession(Guid userId, string userName, string accessToken) { }
    public void ClearSession() { }

    public StubSessionServiceForSearch(Guid userId) => CurrentUserId = userId;
}

[Trait("Category", "Unit")]
public sealed class LocalSearchServiceTests : IAsyncDisposable
{
    private readonly SqliteConnection _conn;
    private readonly LocalSearchService _search;
    private readonly Guid _ownerId = Guid.NewGuid();

    public LocalSearchServiceTests()
    {
        _conn = new SqliteConnection("Data Source=:memory:");
        _conn.Open();
        DatabaseService.ApplySchemaForTestAsync(_conn).GetAwaiter().GetResult();
        _search = new LocalSearchService(_conn, new StubSessionServiceForSearch(_ownerId));
    }

    public async ValueTask DisposeAsync()
    {
        await _conn.DisposeAsync();
    }

    private void InsertTestMessage(
        Guid chatId,
        Guid messageId,
        string senderId,
        string plaintext,
        string chatName = "Test Chat",
        string sentAt = "2024-01-01T00:00:00Z")
    {
        using var chatCmd = _conn.CreateCommand();
        chatCmd.CommandText = """
            INSERT OR IGNORE INTO chats (id, owner_user_id, name, type, last_message_preview, unread_count, updated_at)
            VALUES (@chatId, @ownerId, @chatName, 1, NULL, 0, @updatedAt)
            """;
        chatCmd.Parameters.AddWithValue("@chatId", chatId.ToString());
        chatCmd.Parameters.AddWithValue("@ownerId", _ownerId.ToString());
        chatCmd.Parameters.AddWithValue("@chatName", chatName);
        chatCmd.Parameters.AddWithValue("@updatedAt", sentAt);
        chatCmd.ExecuteNonQuery();

        using var msgCmd = _conn.CreateCommand();
        msgCmd.CommandText = """
            INSERT OR IGNORE INTO messages
                (id, owner_user_id, chat_id, sender_id, client_message_id, protocol_version,
                 encrypted_payload, key_envelope, encryption_algorithm, sent_at, plaintext_body)
            VALUES
                (@id, @ownerId, @chatId, @senderId, @id, 1,
                 'enc', 'env', 'signal-protocol-v1', @sentAt, @plaintext)
            """;
        msgCmd.Parameters.AddWithValue("@id", messageId.ToString());
        msgCmd.Parameters.AddWithValue("@ownerId", _ownerId.ToString());
        msgCmd.Parameters.AddWithValue("@chatId", chatId.ToString());
        msgCmd.Parameters.AddWithValue("@senderId", senderId);
        msgCmd.Parameters.AddWithValue("@sentAt", sentAt);
        msgCmd.Parameters.AddWithValue("@plaintext", plaintext);
        msgCmd.ExecuteNonQuery();
    }


    [Fact]
    public async Task SearchAsync_ReturnsMatchingMessage()
    {
        var chatId = Guid.NewGuid();
        var msgId = Guid.NewGuid();
        InsertTestMessage(chatId, msgId, "sender1", "hello world");

        var results = await _search.SearchAsync("hello");

        Assert.Single(results);
        Assert.Equal(msgId, results[0].MessageId);
        Assert.Contains("[hello]", results[0].Snippet);
    }


    [Fact]
    public async Task SearchAsync_FiltersByChatId()
    {
        var chatA = Guid.NewGuid();
        var chatB = Guid.NewGuid();
        var msgA = Guid.NewGuid();
        var msgB = Guid.NewGuid();

        InsertTestMessage(chatA, msgA, "sender1", "hello from chat A", "Chat A");
        InsertTestMessage(chatB, msgB, "sender2", "hello from chat B", "Chat B");

        var results = await _search.SearchAsync("hello", chatId: chatA);

        Assert.Single(results);
        Assert.Equal(msgA, results[0].MessageId);
        Assert.Equal(chatA, results[0].ChatId);
    }


    [Fact]
    public async Task SearchAsync_EmptyQuery_ReturnsEmptyList()
    {
        var chatId = Guid.NewGuid();
        InsertTestMessage(chatId, Guid.NewGuid(), "sender1", "hello world");

        var results = await _search.SearchAsync("");

        Assert.Empty(results);
    }


    [Fact]
    public async Task SearchAsync_WhitespaceQuery_ReturnsEmptyList()
    {
        var chatId = Guid.NewGuid();
        InsertTestMessage(chatId, Guid.NewGuid(), "sender1", "hello world");

        var results = await _search.SearchAsync("   ");

        Assert.Empty(results);
    }


    [Fact]
    public async Task SearchAsync_SpecialCharacters_NoException()
    {
        var chatId = Guid.NewGuid();
        InsertTestMessage(chatId, Guid.NewGuid(), "sender1", "hello world");

        var ex = await Record.ExceptionAsync(() => _search.SearchAsync("hello \"world"));

        Assert.Null(ex);
    }


    [Fact]
    public async Task SearchAsync_NoMatches_ReturnsEmptyList()
    {
        var chatId = Guid.NewGuid();
        InsertTestMessage(chatId, Guid.NewGuid(), "sender1", "hello world");

        var results = await _search.SearchAsync("xyznonexistent");

        Assert.Empty(results);
    }


    [Fact]
    public async Task SearchAsync_RanksByBm25()
    {
        var chatId = Guid.NewGuid();
        var msgLow = Guid.NewGuid();
        var msgHigh = Guid.NewGuid();

        InsertTestMessage(chatId, msgLow, "sender1", "search once here", sentAt: "2024-01-01T00:00:00Z");
        InsertTestMessage(chatId, msgHigh, "sender2", "search search search many times", sentAt: "2024-01-02T00:00:00Z");

        var results = await _search.SearchAsync("search");

        Assert.Equal(2, results.Count);
        Assert.Equal(msgHigh, results[0].MessageId);
    }


    [Fact]
    public async Task SearchAsync_ReturnsChatName()
    {
        var chatId = Guid.NewGuid();
        var msgId = Guid.NewGuid();
        InsertTestMessage(chatId, msgId, "sender1", "hello world", "My Special Chat");

        var results = await _search.SearchAsync("hello");

        Assert.Single(results);
        Assert.Equal("My Special Chat", results[0].ChatName);
    }
}
