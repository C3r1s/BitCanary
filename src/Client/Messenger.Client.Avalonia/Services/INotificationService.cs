namespace Messenger.Client.Avalonia.Services;

public interface INotificationService
{
    /// <summary>
    /// Shows a Windows toast notification if the window is currently minimized and
    /// notifications are enabled. The toast payload contains no plaintext message content.
    /// </summary>
    void ShowIfMinimized(Guid chatId, string? senderName);
}
