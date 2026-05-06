// Сервис клиента BitCanary: сеть, кэш, медиа — «LocalSearchService».
using Microsoft.Data.Sqlite;

namespace Messenger.Client.Avalonia.Services;

public sealed class LocalSearchService(
    SqliteConnection connection,
    IClientSessionService sessionService) : ILocalSearchService
{
    public async Task<IReadOnlyList<SearchResult>> SearchAsync(
        string query,
        Guid? chatId = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return Array.Empty<SearchResult>();

        var sanitised = SanitiseFtsQuery(query);
        if (string.IsNullOrWhiteSpace(sanitised))
            return Array.Empty<SearchResult>();

        try
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                SELECT m.id, m.chat_id, c.name, m.sender_id, m.sent_at,
                       snippet(messages_fts, 0, '[', ']', '...', 20)
                FROM messages_fts
                JOIN messages m ON m.rowid = messages_fts.rowid
                JOIN chats c ON c.id = m.chat_id AND c.owner_user_id = @ownerId
                WHERE messages_fts MATCH @query
                  AND m.owner_user_id = @ownerId
                  AND (@chatId IS NULL OR m.chat_id = @chatId)
                ORDER BY bm25(messages_fts)
                LIMIT 50
                """;
            cmd.Parameters.AddWithValue("@query", sanitised);
            cmd.Parameters.AddWithValue("@ownerId", sessionService.CurrentUserId.ToString());
            cmd.Parameters.AddWithValue("@chatId", chatId.HasValue ? chatId.Value.ToString() : (object)DBNull.Value);

            var results = new List<SearchResult>();
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                results.Add(new SearchResult(
                    MessageId: Guid.Parse(reader.GetString(0)),
                    ChatId: Guid.Parse(reader.GetString(1)),
                    ChatName: reader.GetString(2),
                    SenderDisplayName: reader.GetString(3),
                    SentAt: DateTimeOffset.Parse(
                        reader.GetString(4),
                        null,
                        System.Globalization.DateTimeStyles.RoundtripKind),
                    Snippet: reader.IsDBNull(5) ? string.Empty : reader.GetString(5)
                ));
            }
            return results;
        }
        catch (SqliteException)
        {
            return Array.Empty<SearchResult>();
        }
    }

    private static string SanitiseFtsQuery(string raw)
    {
        var cleaned = raw
            .Replace("\"", " ")
            .Replace("*", " ")
            .Replace("(", " ")
            .Replace(")", " ")
            .Replace("-", " ");

        var tokens = cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return string.Join(' ', tokens);
    }
}
