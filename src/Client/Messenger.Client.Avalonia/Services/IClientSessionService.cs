// Сервис клиента BitCanary: сеть, кэш, медиа — «IClientSessionService».
namespace Messenger.Client.Avalonia.Services;

public interface IClientSessionService
{
    string ApiBaseUrl { get; }
    string? AccessToken { get; }
    Guid CurrentUserId { get; }
    string UserName { get; }
    bool IsAuthenticated { get; }

    void SetSession(Guid userId, string userName, string accessToken);

    void ClearSession();
}
