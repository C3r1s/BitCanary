using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Messenger.Client.Avalonia.ViewModels;

public sealed partial class MessageInputViewModel : ViewModelBase
{
    private readonly Func<string, Task> _sendAsync;
    private readonly Func<Guid?, bool, Task> _typingChangedAsync;
    private readonly Func<Guid?> _getCurrentChatId;
    private readonly Func<bool> _getIsBlockedForKeyVerification;

    [ObservableProperty]
    private string _text = string.Empty;

    [ObservableProperty]
    private bool _isSending;

    [ObservableProperty]
    private bool _isBlockedForKeyVerification;

    [ObservableProperty]
    private string? _pendingAttachmentName;

    public IAsyncRelayCommand SendCommand { get; }
    public IRelayCommand<string> InsertEmojiCommand { get; }
    public IRelayCommand ClearAttachmentCommand { get; }

    public MessageInputViewModel(
        Func<string, Task> sendAsync,
        Func<Guid?, bool, Task> typingChangedAsync,
        Func<Guid?> getCurrentChatId,
        Func<bool> getIsBlockedForKeyVerification)
    {
        _sendAsync = sendAsync;
        _typingChangedAsync = typingChangedAsync;
        _getCurrentChatId = getCurrentChatId;
        _getIsBlockedForKeyVerification = getIsBlockedForKeyVerification;
        SendCommand = new AsyncRelayCommand(SendAsync, CanSend);
        InsertEmojiCommand = new RelayCommand<string>(emoji =>
        {
            if (emoji is not null) Text += emoji;
        });
        ClearAttachmentCommand = new RelayCommand(() =>
        {
            PendingAttachmentName = null;
            SendCommand.NotifyCanExecuteChanged();
        });
    }

    /// <summary>Called from code-behind after the user picks a file.</summary>
    public void AttachFile(string fileName)
    {
        PendingAttachmentName = fileName;
        SendCommand.NotifyCanExecuteChanged();
    }

    partial void OnTextChanged(string value)
    {
        SendCommand.NotifyCanExecuteChanged();
        _ = _typingChangedAsync(_getCurrentChatId(), !string.IsNullOrWhiteSpace(value));
    }

    /// <summary>Notifies SendCommand that block state may have changed.</summary>
    public void NotifyBlockStateChanged()
    {
        SendCommand.NotifyCanExecuteChanged();
    }

    private bool CanSend() =>
        (!string.IsNullOrWhiteSpace(Text) || PendingAttachmentName is not null)
        && !IsSending
        && !_getIsBlockedForKeyVerification();

    private async Task SendAsync()
    {
        var text = Text.Trim();
        if (PendingAttachmentName is not null)
            text = string.IsNullOrWhiteSpace(text)
                ? $"[File: {PendingAttachmentName}]"
                : $"{text}\n[File: {PendingAttachmentName}]";

        if (string.IsNullOrWhiteSpace(text))
            return;

        IsSending = true;
        SendCommand.NotifyCanExecuteChanged();

        try
        {
            await _sendAsync(text);
            Text = string.Empty;
            PendingAttachmentName = null;
        }
        finally
        {
            IsSending = false;
            SendCommand.NotifyCanExecuteChanged();
            await _typingChangedAsync(_getCurrentChatId(), false);
        }
    }
}
