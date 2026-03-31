using CommunityToolkit.Mvvm.Input;

namespace Messenger.Client.Avalonia.ViewModels;

public sealed partial class MessageItemViewModel : ViewModelBase
{
    public required Guid Id { get; init; }
    public required string SenderDisplayName { get; init; }
    public required string DisplayText { get; init; }
    public required string Timestamp { get; init; }
    public required bool IsOutgoing { get; init; }

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
