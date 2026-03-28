using Messenger.Application.Abstractions;
using Messenger.Shared.Contracts.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Messenger.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/users")]
public sealed class UsersController(IUserService userService) : ControllerBase
{
    [HttpGet("me")]
    public Task<UserProfileDto> GetMe(CancellationToken cancellationToken) =>
        userService.GetCurrentProfileAsync(cancellationToken);

    [HttpPut("me")]
    public Task<UserProfileDto> UpdateMe(UpdateProfileRequest request, CancellationToken cancellationToken) =>
        userService.UpdateProfileAsync(request, cancellationToken);

    [HttpGet("me/settings")]
    public Task<UserSettingsDto> GetSettings(CancellationToken cancellationToken) =>
        userService.GetSettingsAsync(cancellationToken);

    [HttpPut("me/settings")]
    public Task<UserSettingsDto> UpdateSettings(UpdateSettingsRequest request, CancellationToken cancellationToken) =>
        userService.UpdateSettingsAsync(request, cancellationToken);
}
