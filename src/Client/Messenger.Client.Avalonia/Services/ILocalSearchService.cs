// Сервис клиента BitCanary: сеть, кэш, медиа — «ILocalSearchService».
namespace Messenger.Client.Avalonia.Services;

public sealed record SearchResult(
    Guid MessageId,
    Guid ChatId,
    string ChatName,
    string SenderDisplayName,
    DateTimeOffset SentAt,
    string Snippet
);

public interface ILocalSearchService
{
    Task<IReadOnlyList<SearchResult>> SearchAsync(
        string query,
        Guid? chatId = null,
        CancellationToken cancellationToken = default);
}
