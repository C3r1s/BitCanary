namespace Messenger.Application.Abstractions;

public interface ICurrentUserContext
{
    Guid UserId { get; }
    string UserName { get; }
    bool IsAuthenticated { get; }
}
