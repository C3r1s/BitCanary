// Общее перечисление/константа BitCanary: «CallSignalKind» (клиент + сервер).
namespace Messenger.Shared.Contracts;

public enum CallSignalKind
{
    Offer = 1,
    Answer = 2,
    IceCandidate = 3
}
