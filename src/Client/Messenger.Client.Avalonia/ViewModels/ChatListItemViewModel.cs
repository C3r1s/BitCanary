using CommunityToolkit.Mvvm.ComponentModel;
using Messenger.Shared.Contracts;

namespace Messenger.Client.Avalonia.ViewModels;

public sealed partial class ChatListItemViewModel : ViewModelBase
{
    public required Guid Id { get; init; }
    public required string Title { get; init; }
    public required ChatType Type { get; init; }

    /// <summary>
    /// UserId of the peer in a 1-to-1 chat. Guid.Empty for group chats.
    /// Used to determine the recipient when sending encrypted messages.
    /// </summary>
    public Guid PeerUserId { get; init; }

    /// <summary>True for group chats (ChatType.Group = 2).</summary>
    public bool IsGroupChat => Type == ChatType.Group;

    /// <summary>Number of members — populated from ChatSummaryDto.Members.Count for group chats.</summary>
    public int MemberCount { get; init; }

    [ObservableProperty]
    private string _subtitle = string.Empty;

    /// <summary>Formatted relative timestamp string (e.g. "5m ago", "14:32", "Mon", "Mar 26").</summary>
    [ObservableProperty]
    private string _lastActivity = string.Empty;

    [ObservableProperty]
    private int _unreadCount;

    /// <summary>True when <see cref="UnreadCount"/> is greater than zero; drives unread badge visibility.</summary>
    public bool HasUnreadMessages => UnreadCount > 0;

    /// <summary>Called by CommunityToolkit.Mvvm when UnreadCount changes; raises HasUnreadMessages notification.</summary>
    partial void OnUnreadCountChanged(int value)
    {
        OnPropertyChanged(nameof(HasUnreadMessages));
    }
}
