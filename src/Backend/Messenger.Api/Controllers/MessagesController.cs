using Messenger.Application.Abstractions;
using Messenger.Application.Messages;
using Messenger.Shared.Contracts.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Messenger.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/messages")]
public sealed class MessagesController(IMessageService messageService) : ControllerBase
{
    [HttpPost]
    public Task<MessageDto> SendMessage([FromBody] SendMessageRequest request, CancellationToken cancellationToken)
    {
        var command = new SendMessageCommand(
            request.ChatId,
            request.ClientMessageId,
            request.Kind,
            request.EncryptedPayload,
            request.EncryptionAlgorithm,
            request.KeyEnvelope,
            request.MediaId,
            request.ReplyToMessageId,
            request.MetadataJson,
            request.ProtocolVersion);

        return messageService.SendAsync(command, cancellationToken);
    }
}
