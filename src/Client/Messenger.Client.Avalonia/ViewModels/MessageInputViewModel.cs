// Состояние и команды UI BitCanary для «MessageInputViewModel».
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Messenger.Client.Avalonia.Models;
using Messenger.Client.Avalonia.Services;

namespace Messenger.Client.Avalonia.ViewModels;

public sealed partial class MessageInputViewModel : ViewModelBase
{
    private readonly Func<ChatSendPayload, Task> _sendAsync;
    private readonly Func<Guid?, bool, Task> _typingChangedAsync;
    private readonly Func<Guid?> _getCurrentChatId;
    private readonly Func<bool> _getIsBlockedForKeyVerification;

    private IStorageFile? _pendingAttachment;

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
        Func<ChatSendPayload, Task> sendAsync,
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
            _pendingAttachment = null;
            PendingAttachmentName = null;
            SendCommand.NotifyCanExecuteChanged();
        });
    }

    public void AttachFile(IStorageFile file)
    {
        _pendingAttachment = file;
        PendingAttachmentName = file.Name;
        SendCommand.NotifyCanExecuteChanged();
    }

    partial void OnTextChanged(string value)
    {
        SendCommand.NotifyCanExecuteChanged();
        _ = _typingChangedAsync(_getCurrentChatId(), !string.IsNullOrWhiteSpace(value));
    }

    public void NotifyBlockStateChanged()
    {
        SendCommand.NotifyCanExecuteChanged();
    }

    private bool CanSend() =>
        (!string.IsNullOrWhiteSpace(Text) || _pendingAttachment is not null)
        && !IsSending
        && !_getIsBlockedForKeyVerification();

    private async Task SendAsync()
    {
        var trimmed = Text.Trim();
        IStorageFile? imageFile = null;
        string textForSend;

        if (_pendingAttachment is not null)
        {
            if (ImageSendFormats.IsImageFileName(_pendingAttachment.Name))
            {
                imageFile = _pendingAttachment;
                textForSend = trimmed;
            }
            else
            {
                textForSend = string.IsNullOrWhiteSpace(trimmed)
                    ? $"[File: {_pendingAttachment.Name}]"
                    : $"{trimmed}\n[File: {_pendingAttachment.Name}]";
            }
        }
        else
        {
            textForSend = trimmed;
        }

        if (string.IsNullOrWhiteSpace(textForSend) && imageFile is null)
            return;

        IsSending = true;
        SendCommand.NotifyCanExecuteChanged();

        try
        {
            await _sendAsync(new ChatSendPayload(textForSend, imageFile));
            Text = string.Empty;
            _pendingAttachment = null;
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
