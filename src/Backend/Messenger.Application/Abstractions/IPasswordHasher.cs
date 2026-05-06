// Абстракция слоя Application BitCanary: «IPasswordHasher».
namespace Messenger.Application.Abstractions;

public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string password, string passwordHash);
}
