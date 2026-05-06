// Загрузка и выдача медиафайлов для сообщений BitCanary.
using Messenger.Application.Abstractions;
using Messenger.Shared.Contracts.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Messenger.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/media")]
public sealed class MediaController(IMediaService mediaService) : ControllerBase
{
    [HttpPost("upload")]
    [RequestSizeLimit(250_000_000)]
    public async Task<ActionResult<MediaUploadResponse>> Upload(IFormFile file, CancellationToken cancellationToken)
    {
        if (file.Length == 0)
        {
            return BadRequest(new { error = "File is empty." });
        }

        await using var stream = file.OpenReadStream();

        var response = await mediaService.UploadAsync(
            file.FileName,
            file.ContentType,
            file.Length,
            stream,
            cancellationToken);

        return Ok(response);
    }

    [HttpGet("{mediaId:guid}")]
    public async Task<IActionResult> Download(Guid mediaId, CancellationToken cancellationToken)
    {
        var result = await mediaService.DownloadAsync(mediaId, cancellationToken);
        return File(result.Content, result.ContentType, result.FileName);
    }
}
