namespace Messenger.Client.Avalonia.Services;

public interface IClientSessionService
{
    string ApiBaseUrl { get; }
    string? AccessToken { get; }
    Guid CurrentUserId { get; }
    string UserName { get; }
    bool IsAuthenticated { get; }

    /// <summary>Persist session data after a successful login.</summary>
    void SetSession(Guid userId, string userName, string accessToken);

    /// <summary>Wipe the in-memory and on-disk session (logout).</summary>
    void ClearSession();
}
