using System.Net;
using Messenger.Application.Abstractions;

namespace Messenger.Application.Common;

public static class CurrentUserContextExtensions
{
    public static Guid RequireUserId(this ICurrentUserContext currentUser)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId == Guid.Empty)
        {
            throw new AppException("Authentication is required.", HttpStatusCode.Unauthorized);
        }

        return currentUser.UserId;
    }
}
