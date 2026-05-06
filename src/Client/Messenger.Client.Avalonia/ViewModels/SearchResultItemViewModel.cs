// Состояние и команды UI BitCanary для «SearchResultItemViewModel».
using Messenger.Client.Avalonia.Services;

namespace Messenger.Client.Avalonia.ViewModels;

public sealed partial class SearchResultItemViewModel : ViewModelBase
{
    public Guid MessageId { get; }
    public Guid ChatId { get; }

    public string ChatName { get; }

    public string Snippet { get; }

    public string Timestamp { get; }

    public string SenderDisplayName { get; }

    public SearchResultItemViewModel(SearchResult result)
    {
        MessageId = result.MessageId;
        ChatId = result.ChatId;
        ChatName = result.ChatName;
        Snippet = result.Snippet;
        SenderDisplayName = result.SenderDisplayName;
        Timestamp = result.SentAt.LocalDateTime.ToString("MMM d, h:mm tt");
    }
}
