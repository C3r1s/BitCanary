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

    [ObservableProperty]
    private string _subtitle = string.Empty;

    [ObservableProperty]
    private string _lastActivity = string.Empty;

    [ObservableProperty]
    private int _unreadCount;
}
