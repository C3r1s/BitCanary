// Сервис клиента BitCanary: сеть, кэш, медиа — «INotificationService».
namespace Messenger.Client.Avalonia.Services;

public interface INotificationService
{
    void ShowIfMinimized(Guid chatId, string? senderName);
}
