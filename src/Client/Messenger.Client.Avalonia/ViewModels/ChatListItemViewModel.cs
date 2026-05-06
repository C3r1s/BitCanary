// Состояние и команды UI BitCanary для «ChatListItemViewModel».
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Messenger.Shared.Contracts;

namespace Messenger.Client.Avalonia.ViewModels;

public sealed partial class ChatListItemViewModel : ViewModelBase
{
    public IAsyncRelayCommand DeleteChatCommand { get; }
    public IAsyncRelayCommand ClearMessagesCommand { get; }

    public ChatListItemViewModel(Func<Guid, Task> deleteChatAsync, Func<Guid, Task> clearMessagesAsync)
    {
        DeleteChatCommand = new AsyncRelayCommand(() => deleteChatAsync(Id));
        ClearMessagesCommand = new AsyncRelayCommand(() => clearMessagesAsync(Id));
    }

    public required Guid Id { get; init; }
    public required string Title { get; init; }
    public required ChatType Type { get; init; }

    public Guid PeerUserId { get; init; }

    public bool IsGroupChat => Type != ChatType.Direct;

    public int MemberCount { get; init; }

    [ObservableProperty]
    private string _subtitle = string.Empty;

    [ObservableProperty]
    private string _lastActivity = string.Empty;

    [ObservableProperty]
    private int _unreadCount;

    public bool HasUnreadMessages => UnreadCount > 0;

    partial void OnUnreadCountChanged(int value)
    {
        OnPropertyChanged(nameof(HasUnreadMessages));
    }
}
