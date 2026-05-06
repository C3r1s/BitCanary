// Общее перечисление/константа BitCanary: «MessageStatus» (клиент + сервер).
namespace Messenger.Shared.Contracts;

public enum MessageStatus
{
    Sending = 0,
    Delivered = 1,
    Read = 2,
    Failed = 3
}
