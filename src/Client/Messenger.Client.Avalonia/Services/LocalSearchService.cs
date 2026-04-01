using Microsoft.Data.Sqlite;

namespace Messenger.Client.Avalonia.Services;

/// <summary>
/// FTS5-backed implementation of <see cref="ILocalSearchService"/>.
/// Executes parameterised BM25-ranked MATCH queries against the <c>messages_fts</c>
/// virtual table created by <see cref="DatabaseService.MigrateToV3Async"/>.
///
/// Constructor takes the shared <see cref="SqliteConnection"/> singleton (same
/// connection used by <see cref="LocalMessageRepository"/> — SQLite WAL supports
/// concurrent readers per Plan 03-03 decision).
/// </summary>
public sealed class LocalSearchService(SqliteConnection connection) : ILocalSearchService
{
    public async Task<IReadOnlyList<SearchResult>> SearchAsync(
        string query,
        Guid? chatId = null,
        CancellationToken cancellationToken = default)
    {
        // Guard: empty / whitespace query — return immediately, no DB round-trip
        if (string.IsNullOrWhiteSpace(query))
            return Array.Empty<SearchResult>();

        // Sanitise: strip FTS5 operator characters that could cause syntax errors,
        // then split and rejoin to normalise whitespace (Pitfall 3 in RESEARCH.md).
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
                JOIN chats c ON c.id = m.chat_id
                WHERE messages_fts MATCH @query
                  AND (@chatId IS NULL OR m.chat_id = @chatId)
                ORDER BY bm25(messages_fts)
                LIMIT 50
                """;
            cmd.Parameters.AddWithValue("@query", sanitised);
            cmd.Parameters.AddWithValue("@chatId", chatId.HasValue ? chatId.Value.ToString() : (object)DBNull.Value);

            var results = new List<SearchResult>();
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                results.Add(new SearchResult(
                    MessageId: Guid.Parse(reader.GetString(0)),
                    ChatId: Guid.Parse(reader.GetString(1)),
                    ChatName: reader.GetString(2),
                    SenderDisplayName: reader.GetString(3),    // sender_id used as fallback
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
            // If sanitisation didn't fully neutralise a malformed query, swallow the
            // FTS5 syntax error and return empty rather than surfacing a crash (Pitfall 4).
            return Array.Empty<SearchResult>();
        }
    }

    /// <summary>
    /// Strips FTS5 operator characters that could cause syntax errors, splits on
    /// whitespace, and rejoins with spaces. Returns an empty string if nothing remains.
    /// </summary>
    private static string SanitiseFtsQuery(string raw)
    {
        // Strip characters that have special meaning in FTS5 syntax
        var cleaned = raw
            .Replace("\"", " ")
            .Replace("*", " ")
            .Replace("(", " ")
            .Replace(")", " ")
            .Replace("-", " ");

        // Split on whitespace, filter empty tokens, rejoin
        var tokens = cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return string.Join(' ', tokens);
    }
}
