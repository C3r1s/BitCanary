// Общее перечисление/константа BitCanary: «ConnectionState» (клиент + сервер).
namespace Messenger.Shared.Contracts;

public enum ConnectionState
{
    Online = 0,
    Reconnecting = 1,
    Offline = 2
}
