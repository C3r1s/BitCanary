// Доступ к Claims текущего пользователя в запросе API BitCanary.
using System.Security.Claims;
using Messenger.Application.Abstractions;

namespace Messenger.Api.Services;

public sealed class CurrentUserContext(IHttpContextAccessor httpContextAccessor) : ICurrentUserContext
{
    public Guid UserId =>
        Guid.TryParse(
            httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier),
            out var userId)
            ? userId
            : Guid.Empty;

    public string UserName =>
        httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.Name) ?? string.Empty;

    public bool IsAuthenticated =>
        httpContextAccessor.HttpContext?.User.Identity?.IsAuthenticated ?? false;
}
