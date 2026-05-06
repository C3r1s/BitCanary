// Состояние и команды UI BitCanary для «ChatListViewModel».
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

    [ObservableProperty]
    private SearchViewModel? _search;

    public UserSearchViewModel? UserSearch { get; set; }

    public GroupCreationViewModel? GroupCreation { get; set; }

    public bool IsInNormalMode => !IsSearchMode && !IsUserSearchMode && !IsGroupCreationMode;

    private bool _suppressModeReentry;

    partial void OnIsSearchModeChanged(bool value)
    {
        OnPropertyChanged(nameof(IsInNormalMode));
        if (!value || _suppressModeReentry) return;
        _suppressModeReentry = true;
        try
        {
            if (IsUserSearchMode)
            {
                IsUserSearchMode = false;
                UserSearch?.Reset();
            }
            if (IsGroupCreationMode)
            {
                IsGroupCreationMode = false;
                GroupCreation?.Reset();
            }
        }
        finally { _suppressModeReentry = false; }
    }

    partial void OnIsUserSearchModeChanged(bool value)
    {
        OnPropertyChanged(nameof(IsInNormalMode));
        if (!value || _suppressModeReentry) return;
        _suppressModeReentry = true;
        try
        {
            if (IsSearchMode)
            {
                IsSearchMode = false;
                Search?.Reset();
            }
            if (IsGroupCreationMode)
            {
                IsGroupCreationMode = false;
                GroupCreation?.Reset();
            }
        }
        finally { _suppressModeReentry = false; }
    }

    partial void OnIsGroupCreationModeChanged(bool value)
    {
        OnPropertyChanged(nameof(IsInNormalMode));
        if (!value || _suppressModeReentry) return;
        _suppressModeReentry = true;
        try
        {
            if (IsSearchMode)
            {
                IsSearchMode = false;
                Search?.Reset();
            }
            if (IsUserSearchMode)
            {
                IsUserSearchMode = false;
                UserSearch?.Reset();
            }
        }
        finally { _suppressModeReentry = false; }
    }

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
