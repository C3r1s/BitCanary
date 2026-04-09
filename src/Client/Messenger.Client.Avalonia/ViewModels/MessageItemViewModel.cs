using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Messenger.Shared.Contracts;

namespace Messenger.Client.Avalonia.ViewModels;

public sealed partial class MessageItemViewModel : ViewModelBase
{
    public required Guid Id { get; init; }
    public required string SenderDisplayName { get; init; }
    public required string DisplayText { get; init; }
    public required string Timestamp { get; init; }
    public required bool IsOutgoing { get; init; }

    /// <summary>Stable Guid assigned at creation. Reused on retry for server-side dedup.</summary>
    public required Guid ClientMessageId { get; init; }

    /// <summary>Set by per-chat find-bar logic to show accent left-border on matching messages.</summary>
    [ObservableProperty]
    private bool _isHighlighted;

    /// <summary>Send status for outgoing messages. Defaults to Sending until updated by SignalR events.</summary>
    [ObservableProperty]
    private MessageStatus _status = MessageStatus.Sending;

    /// <summary>Display glyph corresponding to the current send status.</summary>
    public string StatusGlyph => _status switch
    {
        MessageStatus.Sending   => "\u29D6",  // ⧖
        MessageStatus.Delivered => "\u2713",  // ✓
        MessageStatus.Read      => "\u2713\u2713", // ✓✓
        MessageStatus.Failed    => "\u26A0",  // ⚠
        _                       => string.Empty
    };

    partial void OnStatusChanged(MessageStatus value)
    {
        OnPropertyChanged(nameof(StatusGlyph));
        OnPropertyChanged(nameof(IsFailedOutgoing));
    }

    /// <summary>True when this outgoing message failed to send. Drives Retry button visibility.</summary>
    public bool IsFailedOutgoing => Status == MessageStatus.Failed && IsOutgoing;

    /// <summary>
    /// Delegate injected by MainWindowViewModel. Null on non-outgoing or non-retryable messages.
    /// </summary>
    private Func<Guid, Task>? _retryDelegate;

    /// <summary>Command bound in AXAML. Visible only when IsFailedOutgoing.</summary>
    public IAsyncRelayCommand RetryCommand { get; private set; } = null!;

    /// <summary>Initialise the retry delegate. Called by MainWindowViewModel after construction.</summary>
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

    // Banner support — optional, only set for system banner messages
    public bool IsSystemBanner { get; init; }
    public string? BannerText { get; init; }
    public string? BannerAction1Label { get; init; }
    public string? BannerAction2Label { get; init; }
    public IRelayCommand? BannerAction1Command { get; init; }
    public IRelayCommand? BannerAction2Command { get; init; }

    /// <summary>
    /// Creates a system banner MessageItemViewModel for key change alerts.
    /// </summary>
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
