// Состояние и команды UI BitCanary для «ChatWindowViewModel».
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Messenger.Client.Avalonia.ViewModels;

public sealed partial class ChatWindowViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _title = "Select a chat";

    [ObservableProperty]
    private string _subtitle = "Choose a conversation to view encrypted history.";

    [ObservableProperty]
    private string _typingStatus = string.Empty;

    [ObservableProperty]
    private bool _isSessionVerified;

    [ObservableProperty]
    private bool _isUnverified;

    [ObservableProperty]
    private bool _isFindBarVisible;

    [ObservableProperty]
    private string _findQuery = string.Empty;

    [ObservableProperty]
    private string _findMatchSummary = string.Empty;

    [ObservableProperty]
    private bool _isGroupChat;

    public bool IsDirectChat => !IsGroupChat;

    public bool ShowUnverifiedBadge => IsDirectChat && !IsSessionVerified;

    public bool ShowVerifiedBadge => IsDirectChat && IsSessionVerified;

    partial void OnIsGroupChatChanged(bool value)
    {
        OnPropertyChanged(nameof(IsDirectChat));
        OnPropertyChanged(nameof(ShowUnverifiedBadge));
        OnPropertyChanged(nameof(ShowVerifiedBadge));
    }

    partial void OnIsUnverifiedChanged(bool value) => OnPropertyChanged(nameof(ShowUnverifiedBadge));
    partial void OnIsSessionVerifiedChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowUnverifiedBadge));
        OnPropertyChanged(nameof(ShowVerifiedBadge));
    }

    [ObservableProperty]
    private bool _isGroupInfoVisible;

    [ObservableProperty]
    private int _groupMemberCount;

    public IRelayCommand? ShowSafetyNumberCommand { get; set; }

    public GroupInfoViewModel? GroupInfo { get; set; }

    public IRelayCommand? ShowGroupInfoCommand { get; set; }

    public IRelayCommand? CloseGroupInfoCommand { get; set; }

    public IRelayCommand OpenFindBarCommand { get; }

    public IRelayCommand CloseFindBarCommand { get; }

    public ObservableCollection<MessageItemViewModel> Messages { get; } = new();

    public bool HasNoMessages => Messages.Count == 0;

    public ChatWindowViewModel()
    {
        Messages.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasNoMessages));
        OpenFindBarCommand = new RelayCommand(() => IsFindBarVisible = true);
        CloseFindBarCommand = new RelayCommand(() =>
        {
            IsFindBarVisible = false;
            FindQuery = string.Empty;
            ClearHighlights();
            FindMatchSummary = string.Empty;
        });
    }

    partial void OnFindQueryChanged(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            ClearHighlights();
            FindMatchSummary = string.Empty;
            return;
        }

        var matchCount = 0;
        foreach (var message in Messages)
        {
            if (!message.IsSystemBanner &&
                message.DisplayText.Contains(value, StringComparison.OrdinalIgnoreCase))
            {
                message.IsHighlighted = true;
                matchCount++;
            }
            else
            {
                message.IsHighlighted = false;
            }
        }

        FindMatchSummary = matchCount == 1 ? "1 match" : $"{matchCount} matches";
    }

    private void ClearHighlights()
    {
        foreach (var message in Messages)
        {
            message.IsHighlighted = false;
        }
    }
}
