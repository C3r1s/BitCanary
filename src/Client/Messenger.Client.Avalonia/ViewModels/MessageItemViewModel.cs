// Состояние и команды UI BitCanary для «MessageItemViewModel».
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Messenger.Shared.Contracts;

namespace Messenger.Client.Avalonia.ViewModels;

public sealed partial class MessageItemViewModel : ViewModelBase
{
    public required Guid Id { get; init; }
    public required string SenderDisplayName { get; init; }
    public required string DisplayText { get; init; }

    public MessageKind MessageKind { get; init; } = MessageKind.Text;

    public Guid? MediaId { get; init; }

    private Bitmap? _inlineImage;

    public Bitmap? InlineImage
    {
        get => _inlineImage;
        set
        {
            if (ReferenceEquals(_inlineImage, value)) return;
            var old = _inlineImage;
            _inlineImage = value;
            old?.Dispose();
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasInlineImage));
            OnPropertyChanged(nameof(CaptionVisible));
            OnPropertyChanged(nameof(ShowTextGlyphRow));
            OnPropertyChanged(nameof(ShowRetryButton));
        }
    }

    public bool HasInlineImage => InlineImage is not null;

    public bool CaptionVisible =>
        !HasInlineImage
        || DisplayText.Trim('\u200b', ' ', '\t', '\r', '\n').Length > 0;

    public bool ShowTextGlyphRow => !HasInlineImage || CaptionVisible || IsEncrypted;

    public bool ShowRetryButton => IsFailedOutgoing && MessageKind == MessageKind.Text;
    public required string Timestamp { get; init; }
    public required bool IsOutgoing { get; init; }
    public bool IsEncrypted { get; init; }
    public bool IsGroupChat { get; init; }
    public bool ShowEncryptionGlyph => IsEncrypted && !IsGroupChat;
    public string EncryptionGlyph => IsEncrypted ? "\U0001F512" : string.Empty;

    public required Guid ClientMessageId { get; init; }

    [ObservableProperty]
    private bool _isHighlighted;

    [ObservableProperty]
    private MessageStatus _status = MessageStatus.Sending;

    public string StatusGlyph => Status switch
    {
        MessageStatus.Sending   => "...",
        MessageStatus.Delivered => "v",
        MessageStatus.Read      => "vv",
        MessageStatus.Failed    => "!",
        _                       => string.Empty
    };

    partial void OnStatusChanged(MessageStatus value)
    {
        OnPropertyChanged(nameof(StatusGlyph));
        OnPropertyChanged(nameof(IsFailedOutgoing));
        OnPropertyChanged(nameof(ShowRetryButton));
    }

    public bool IsFailedOutgoing => Status == MessageStatus.Failed && IsOutgoing;

    private Func<Guid, Task>? _retryDelegate;

    public IAsyncRelayCommand RetryCommand { get; private set; } = null!;

    public void SetRetryDelegate(Func<Guid, Task> retryDelegate)
    {
        _retryDelegate = retryDelegate;
        RetryCommand = new AsyncRelayCommand(ExecuteRetryAsync);
        OnPropertyChanged(nameof(RetryCommand));
    }

    private async Task ExecuteRetryAsync()
    {
        if (_retryDelegate is null) return;
        Status = MessageStatus.Sending;
        await _retryDelegate(ClientMessageId);
    }

    public bool IsSystemBanner { get; init; }
    public string? BannerText { get; init; }
    public string? BannerAction1Label { get; init; }
    public string? BannerAction2Label { get; init; }
    public IRelayCommand? BannerAction1Command { get; init; }
    public IRelayCommand? BannerAction2Command { get; init; }

    public static MessageItemViewModel CreateBanner(
        string text,
        IRelayCommand verifyNowCommand,
        IRelayCommand sendAnywayCommand)
    {
        return new MessageItemViewModel
        {
            Id = Guid.NewGuid(),
            ClientMessageId = Guid.NewGuid(),
            SenderDisplayName = "System",
            DisplayText = text,
            Timestamp = string.Empty,
            IsOutgoing = false,
            IsSystemBanner = true,
            BannerText = text,
            BannerAction1Label = "Verify Now",
            BannerAction2Label = "Send Anyway",
            BannerAction1Command = verifyNowCommand,
            BannerAction2Command = sendAnywayCommand
        };
    }
}
