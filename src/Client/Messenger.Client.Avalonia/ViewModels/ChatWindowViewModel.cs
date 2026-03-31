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

    /// <summary>Set by MainWindowViewModel to forward ShowSafetyNumberCommand into the header.</summary>
    public IRelayCommand? ShowSafetyNumberCommand { get; set; }

    public ObservableCollection<MessageItemViewModel> Messages { get; } = new();
}
