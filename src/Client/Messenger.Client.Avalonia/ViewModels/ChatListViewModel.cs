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

    [ObservableProperty]
    private bool _isSearchMode;

    /// <summary>Set by MainWindowViewModel during construction to wire up global search.</summary>
    public SearchViewModel? Search { get; set; }

    public ObservableCollection<ChatListItemViewModel> Chats { get; } = new();

    public IAsyncRelayCommand RefreshCommand { get; }

    public IRelayCommand ToggleSearchCommand { get; }

    public ChatListViewModel(Func<Task> refreshAsync)
    {
        RefreshCommand = new AsyncRelayCommand(refreshAsync);
        ToggleSearchCommand = new RelayCommand(() =>
        {
            if (IsSearchMode)
            {
                IsSearchMode = false;
                Search?.Reset();
            }
            else
            {
                IsSearchMode = true;
                // View code-behind focuses search box when IsSearchMode becomes true
            }
        });
    }
}
