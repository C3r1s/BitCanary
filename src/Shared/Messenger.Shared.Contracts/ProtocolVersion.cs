// Общее перечисление/константа BitCanary: «ProtocolVersion» (клиент + сервер).
namespace Messenger.Shared.Contracts;

public enum ProtocolVersion
{
    LegacyAes = 0,
    SignalProtocol = 1,
    Plaintext = 2,
    NoiseXX = 3
}
