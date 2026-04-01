namespace Messenger.Client.Avalonia.Services;

/// <summary>Result from a full-text search over local decrypted message history.</summary>
public sealed record SearchResult(
    Guid MessageId,
    Guid ChatId,
    string ChatName,
    string SenderDisplayName,
    DateTimeOffset SentAt,
    string Snippet
);

/// <summary>Local FTS5-backed search over decrypted message bodies.</summary>
public interface ILocalSearchService
{
    /// <summary>
    /// Searches the local message history using FTS5 MATCH queries ranked by BM25.
    /// Returns an empty list if <paramref name="query"/> is null, empty, or whitespace.
    /// Special FTS5 characters are sanitised before querying — no <see cref="Microsoft.Data.Sqlite.SqliteException"/>
    /// is raised for malformed query strings.
    /// </summary>
    Task<IReadOnlyList<SearchResult>> SearchAsync(
        string query,
        Guid? chatId = null,
        CancellationToken cancellationToken = default);
}
