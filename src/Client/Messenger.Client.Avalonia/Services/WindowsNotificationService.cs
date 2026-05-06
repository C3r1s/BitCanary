// Сервис клиента BitCanary: сеть, кэш, медиа — «WindowsNotificationService».
using Microsoft.Toolkit.Uwp.Notifications;

namespace Messenger.Client.Avalonia.Services;

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
