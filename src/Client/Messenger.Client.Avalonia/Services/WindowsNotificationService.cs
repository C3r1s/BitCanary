using Microsoft.Toolkit.Uwp.Notifications;

namespace Messenger.Client.Avalonia.Services;

/// <summary>
/// Delivers Windows toast notifications for incoming messages.
/// Uses ToastNotificationManagerCompat (non-obsolete path in 7.1.x) — NOT NotificationActivator.
/// </summary>
public sealed class WindowsNotificationService : INotificationService
{
    private readonly Func<bool> _isMinimized;
    private readonly Func<bool> _showNotifications;
    private readonly Func<bool> _showSenderName;

    public WindowsNotificationService(
        Func<bool> isMinimized,
        Func<bool> showNotifications,
        Func<bool> showSenderName)
    {
        _isMinimized = isMinimized;
        _showNotifications = showNotifications;
        _showSenderName = showSenderName;
    }

    public void ShowIfMinimized(Guid chatId, string? senderName)
    {
        if (!_showNotifications()) return;

        // D-04 (CONTEXT.md): suppress when window is not minimized.
        // D-05 note: D-04 alone fully satisfies D-05 because the window cannot be focused
        // on the active chat while minimized — no additional selectedChatId check is needed.
        if (!_isMinimized()) return;

        var title = (_showSenderName() && senderName is not null)
            ? senderName
            : "Messenger";

        new ToastContentBuilder()
            .AddArgument("chatId", chatId.ToString("D"))
            .AddText(title)
            .AddText("New message")
            .Show();
    }
}
