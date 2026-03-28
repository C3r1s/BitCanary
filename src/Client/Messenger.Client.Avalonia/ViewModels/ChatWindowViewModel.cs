using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Messenger.Client.Avalonia.ViewModels;

public sealed partial class ChatWindowViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _title = "Select a chat";

    [ObservableProperty]
    private string _subtitle = "Choose a conversation to view encrypted history.";

    [ObservableProperty]
    private string _typingStatus = string.Empty;

    public ObservableCollection<MessageItemViewModel> Messages { get; } = new();
}
