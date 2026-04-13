using Messenger.Application.Abstractions;
using Messenger.Shared.Contracts;
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

    [HttpPost("{chatId:guid}/members")]
    public Task<ChatSummaryDto> AddMember(Guid chatId, AddMemberRequest request, CancellationToken ct)
        => chatService.AddMemberAsync(chatId, request.UserId, ct);

    [HttpDelete("{chatId:guid}/members/{userId:guid}")]
    public Task RemoveMember(Guid chatId, Guid userId, CancellationToken ct)
        => chatService.RemoveMemberAsync(chatId, userId, ct);

    [HttpPatch("{chatId:guid}/members/{userId:guid}/role")]
    public Task UpdateMemberRole(Guid chatId, Guid userId, UpdateMemberRoleRequest request, CancellationToken ct)
        => chatService.UpdateMemberRoleAsync(chatId, userId, request.Role, ct);

    [HttpPatch("{chatId:guid}")]
    public Task<ChatSummaryDto> UpdateChat(Guid chatId, UpdateChatRequest request, CancellationToken ct)
        => chatService.UpdateChatAsync(chatId, request, ct);
}
