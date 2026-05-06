// Абстракция слоя Application BitCanary: «ITokenService».
using Messenger.Domain.Entities;

namespace Messenger.Application.Abstractions;

public interface ITokenService
{
    string CreateAccessToken(User user);
}
