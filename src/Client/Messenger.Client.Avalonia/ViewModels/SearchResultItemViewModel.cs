using Messenger.Client.Avalonia.Services;

namespace Messenger.Client.Avalonia.ViewModels;

public sealed partial class SearchResultItemViewModel : ViewModelBase
{
    public Guid MessageId { get; }
    public Guid ChatId { get; }

    /// <summary>Chat name — FontSize 16 SemiBold per UI-SPEC.</summary>
    public string ChatName { get; }

    /// <summary>FTS5 snippet containing [term] delimiters.</summary>
    public string Snippet { get; }

    /// <summary>Formatted timestamp, e.g. "Mar 31, 2:15 PM".</summary>
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
