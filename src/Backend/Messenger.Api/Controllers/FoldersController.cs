// Пользовательские папки для группировки чатов на сервере.
using Messenger.Application.Abstractions;
using Messenger.Shared.Contracts.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Messenger.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/folders")]
public sealed class FoldersController(IChatService chatService) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyCollection<FolderDto>> GetFolders(CancellationToken cancellationToken) =>
        chatService.GetFoldersAsync(cancellationToken);

    [HttpPost]
    public Task<FolderDto> CreateFolder(CreateFolderRequest request, CancellationToken cancellationToken) =>
        chatService.CreateFolderAsync(request, cancellationToken);
}
