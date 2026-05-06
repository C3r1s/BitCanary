// Автотест BitCanary: проверка «MessagesControllerTests».
using FluentAssertions;
using Messenger.Api.Controllers;
using Messenger.Application.Abstractions;
using Messenger.Application.Messages;
using Messenger.Shared.Contracts;
using Messenger.Shared.Contracts.Dtos;
using NSubstitute;

namespace Messenger.Tests.Controllers;

[Trait("req", "WEB04")]
public sealed class MessagesControllerTests
{
    [Fact]
    public async Task SendMessage_WithSignalProtocolVersion_ForwardsToCommand()
    {
        var messageService = Substitute.For<IMessageService>();
        SendMessageCommand? capturedCommand = null;
        messageService.SendAsync(Arg.Do<SendMessageCommand>(c => capturedCommand = c), Arg.Any<CancellationToken>())
            .Returns(CreateMessageDto());

        var controller = new MessagesController(messageService);
        var request = new SendMessageRequest(
            ChatId: Guid.NewGuid(),
            ClientMessageId: Guid.NewGuid(),
            Kind: MessageKind.Text,
            EncryptedPayload: "ciphertext-b64",
            EncryptionAlgorithm: "signal-protocol-v1",
            KeyEnvelope: "session-id",
            MediaId: null,
            ReplyToMessageId: null,
            MetadataJson: "{\"dr\":{\"rk_pub\":\"...\",\"pn\":0,\"n\":0}}",
            ProtocolVersion: ProtocolVersion.SignalProtocol);

        await controller.SendMessage(request, CancellationToken.None);

        capturedCommand.Should().NotBeNull();
        capturedCommand!.ProtocolVersion.Should().Be(ProtocolVersion.SignalProtocol);
    }

    [Fact]
    public async Task SendMessage_WithDefaultProtocolVersion_DefaultsToLegacyAes()
    {
        var messageService = Substitute.For<IMessageService>();
        SendMessageCommand? capturedCommand = null;
        messageService.SendAsync(Arg.Do<SendMessageCommand>(c => capturedCommand = c), Arg.Any<CancellationToken>())
            .Returns(CreateMessageDto());

        var controller = new MessagesController(messageService);
        var request = new SendMessageRequest(
            Guid.NewGuid(),
            Guid.NewGuid(),
            MessageKind.Text,
            "payload",
            "alg",
            null,
            null,
            null,
            null);

        await controller.SendMessage(request, CancellationToken.None);

        capturedCommand.Should().NotBeNull();
        capturedCommand!.ProtocolVersion.Should().Be(ProtocolVersion.LegacyAes);
    }

    private static MessageDto CreateMessageDto() =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            string.Empty,
            MessageKind.Text,
            string.Empty,
            string.Empty,
            string.Empty,
            null,
            null,
            null,
            DateTimeOffset.UtcNow,
            ProtocolVersion.SignalProtocol);
}
