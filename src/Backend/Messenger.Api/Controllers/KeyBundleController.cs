using Messenger.Application.Abstractions;
using Messenger.Application.Common;
using Messenger.Shared.Contracts.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Messenger.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/keys")]
public sealed class KeyBundleController(
    IKeyBundleService keyBundleService,
    ICurrentUserContext currentUser) : ControllerBase
{
    /// <summary>Upload or rotate a key bundle (per D-03, D-05).</summary>
    [HttpPost("bundle")]
    public async Task<IActionResult> UploadBundle(KeyBundleUploadRequest request, CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireUserId();
        var response = await keyBundleService.UploadBundleAsync(userId, request, cancellationToken);

        // D-03: 201 on first upload (no DeviceId in request), 200 on rotation
        if (!request.DeviceId.HasValue)
            return CreatedAtAction(nameof(GetBundle), new { userId }, response);

        return Ok(response);
    }

    /// <summary>Fetch a user's key bundle and atomically claim one OPK (per D-01).</summary>
    [HttpGet("{userId:guid}")]
    public async Task<IActionResult> GetBundle(Guid userId, CancellationToken cancellationToken)
    {
        var bundle = await keyBundleService.GetBundleAsync(userId, cancellationToken);
        if (bundle is null)
            return NotFound();
        return Ok(bundle);
    }

    /// <summary>Replenish one-time prekey pool.</summary>
    [HttpPost("opk/batch")]
    public async Task<IActionResult> ReplenishOpks(OtpkReplenishRequest request, CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireUserId();
        var response = await keyBundleService.ReplenishOpksAsync(userId, request, cancellationToken);
        return Ok(response);
    }
}
