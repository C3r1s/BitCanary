using Messenger.Application.Abstractions;
using Messenger.Application.Common;
using Messenger.Shared.Contracts.Dtos;
using Microsoft.EntityFrameworkCore;

namespace Messenger.Application.Users;

public sealed class UserService(IAppDbContext dbContext, ICurrentUserContext currentUser) : IUserService
{
    public async Task<UserProfileDto> GetCurrentProfileAsync(CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireUserId();

        var user = await dbContext.Users
            .AsNoTracking()
            .SingleAsync(x => x.Id == userId, cancellationToken);

        return user.ToDto();
    }

    public async Task<UserProfileDto> UpdateProfileAsync(UpdateProfileRequest request, CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireUserId();

        var user = await dbContext.Users.SingleAsync(x => x.Id == userId, cancellationToken);
        user.DisplayName = request.DisplayName.Trim();
        user.Bio = string.IsNullOrWhiteSpace(request.Bio) ? null : request.Bio.Trim();
        user.AvatarUrl = string.IsNullOrWhiteSpace(request.AvatarUrl) ? null : request.AvatarUrl.Trim();
        user.PublicKey = request.PublicKey.Trim();

        await dbContext.SaveChangesAsync(cancellationToken);

        return user.ToDto();
    }

    public async Task<UserSettingsDto> GetSettingsAsync(CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireUserId();

        var settings = await dbContext.UserSettings
            .AsNoTracking()
            .SingleAsync(x => x.UserId == userId, cancellationToken);

        return settings.ToDto();
    }

    public async Task<UserSettingsDto> UpdateSettingsAsync(UpdateSettingsRequest request, CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireUserId();

        var settings = await dbContext.UserSettings.SingleAsync(x => x.UserId == userId, cancellationToken);
        settings.ThemePreference = request.ThemePreference;
        settings.SendByEnter = request.SendByEnter;
        settings.UseCompactMode = request.UseCompactMode;
        settings.EnableCustomEmoji = request.EnableCustomEmoji;

        await dbContext.SaveChangesAsync(cancellationToken);

        return settings.ToDto();
    }

    public async Task<IReadOnlyCollection<UserProfileDto>> SearchUsersAsync(
        string query, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Trim().Length < 2)
            return Array.Empty<UserProfileDto>();

        var currentUserId = currentUser.RequireUserId();
        var q = query.Trim().ToLower();

        return await dbContext.Users
            .AsNoTracking()
            .Where(u => u.Id != currentUserId &&
                        (u.UserName.ToLower().Contains(q) ||
                         u.DisplayName.ToLower().Contains(q)))
            .Take(20)
            .Select(u => u.ToDto())
            .ToArrayAsync(cancellationToken);
    }
}
