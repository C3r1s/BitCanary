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

    [ObservableProperty]
    private bool _isUserSearchMode;

    [ObservableProperty]
    private bool _isGroupCreationMode;

    /// <summary>Set by MainWindowViewModel during construction to wire up global search.</summary>
    [ObservableProperty]
    private SearchViewModel? _search;

    /// <summary>Set by MainWindowViewModel during construction to wire up user-directory search.</summary>
    public UserSearchViewModel? UserSearch { get; set; }

    /// <summary>Set by MainWindowViewModel during construction to wire up group creation.</summary>
    public GroupCreationViewModel? GroupCreation { get; set; }

    /// <summary>True when no search or creation mode is active.</summary>
    public bool IsInNormalMode => !IsSearchMode && !IsUserSearchMode && !IsGroupCreationMode;

    partial void OnIsSearchModeChanged(bool _) => OnPropertyChanged(nameof(IsInNormalMode));
    partial void OnIsUserSearchModeChanged(bool _) => OnPropertyChanged(nameof(IsInNormalMode));
    partial void OnIsGroupCreationModeChanged(bool _) => OnPropertyChanged(nameof(IsInNormalMode));

    public ObservableCollection<ChatListItemViewModel> Chats { get; } = new();

    public IAsyncRelayCommand RefreshCommand { get; }

    public IRelayCommand ToggleSearchCommand { get; }

    public IRelayCommand ToggleUserSearchCommand { get; }

    public IRelayCommand ToggleGroupCreationCommand { get; }

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
                IsUserSearchMode = false;   // mutual exclusion per Pitfall 3
                UserSearch?.Reset();
                IsSearchMode = true;
                // View code-behind focuses search box when IsSearchMode becomes true
            }
        });
        ToggleUserSearchCommand = new RelayCommand(() =>
        {
            if (IsUserSearchMode)
            {
                IsUserSearchMode = false;
                UserSearch?.Reset();
            }
            else
            {
                IsSearchMode = false;   // mutual exclusion
                Search?.Reset();
                IsGroupCreationMode = false;
                GroupCreation?.Reset();
                IsUserSearchMode = true;
            }
        });

        ToggleGroupCreationCommand = new RelayCommand(() =>
        {
            if (IsGroupCreationMode)
            {
                IsGroupCreationMode = false;
                GroupCreation?.Reset();
            }
            else
            {
                IsSearchMode = false;
                Search?.Reset();
                IsUserSearchMode = false;
                UserSearch?.Reset();
                IsGroupCreationMode = true;
            }
        });
    }
}
