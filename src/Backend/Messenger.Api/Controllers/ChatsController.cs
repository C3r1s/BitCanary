using Messenger.Application.Abstractions;
using Messenger.Shared.Contracts.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Messenger.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/chats")]
public sealed class ChatsController(IChatService chatService, IMessageService messageService) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyCollection<ChatSummaryDto>> GetChats(CancellationToken cancellationToken) =>
        chatService.GetChatsAsync(cancellationToken);

    [HttpPost]
    public Task<ChatSummaryDto> CreateChat(CreateChatRequest request, CancellationToken cancellationToken) =>
        chatService.CreateChatAsync(request, cancellationToken);

    [HttpGet("{chatId:guid}/messages")]
    public Task<IReadOnlyCollection<MessageDto>> GetMessages(Guid chatId, CancellationToken cancellationToken) =>
        messageService.GetMessagesAsync(chatId, cancellationToken);
}
