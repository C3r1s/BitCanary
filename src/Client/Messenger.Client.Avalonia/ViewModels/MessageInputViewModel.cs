using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Messenger.Client.Avalonia.ViewModels;

public sealed partial class MessageInputViewModel : ViewModelBase
{
    private readonly Func<string, Task> _sendAsync;
    private readonly Func<Guid?, bool, Task> _typingChangedAsync;
    private readonly Func<Guid?> _getCurrentChatId;

    [ObservableProperty]
    private string _text = string.Empty;

    [ObservableProperty]
    private bool _isSending;

    public IAsyncRelayCommand SendCommand { get; }

    public MessageInputViewModel(
        Func<string, Task> sendAsync,
        Func<Guid?, bool, Task> typingChangedAsync,
        Func<Guid?> getCurrentChatId)
    {
        _sendAsync = sendAsync;
        _typingChangedAsync = typingChangedAsync;
        _getCurrentChatId = getCurrentChatId;
        SendCommand = new AsyncRelayCommand(SendAsync, CanSend);
    }

    partial void OnTextChanged(string value)
    {
        SendCommand.NotifyCanExecuteChanged();
        _ = _typingChangedAsync(_getCurrentChatId(), !string.IsNullOrWhiteSpace(value));
    }

    private bool CanSend() => !string.IsNullOrWhiteSpace(Text) && !IsSending;

    private async Task SendAsync()
    {
        var text = Text.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        IsSending = true;
        SendCommand.NotifyCanExecuteChanged();

        try
        {
            await _sendAsync(text);
            Text = string.Empty;
        }
        finally
        {
            IsSending = false;
            SendCommand.NotifyCanExecuteChanged();
            await _typingChangedAsync(_getCurrentChatId(), false);
        }
    }
}
