using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Messenger.Client.Avalonia.ViewModels;

public sealed partial class ChatListViewModel : ViewModelBase
{
    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private ChatListItemViewModel? _selectedChat;

    public ObservableCollection<ChatListItemViewModel> Chats { get; } = new();

    public IAsyncRelayCommand RefreshCommand { get; }

    public ChatListViewModel(Func<Task> refreshAsync)
    {
        RefreshCommand = new AsyncRelayCommand(refreshAsync);
    }
}
