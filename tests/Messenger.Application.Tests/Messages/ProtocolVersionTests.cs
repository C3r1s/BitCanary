using Messenger.Application.Messages;
using Messenger.Shared.Contracts;
using Messenger.Shared.Contracts.Dtos;

namespace Messenger.Application.Tests.Messages;

public sealed class ProtocolVersionTests
{
    [Fact]
    public void SendMessageRequest_DefaultProtocolVersion_IsLegacyAes()
    {
        var request = new SendMessageRequest(
            Guid.NewGuid(),
            Guid.NewGuid(),
            MessageKind.Text,
            "payload",
            "AES",
            "envelope",
            null,
            null,
            null);

        Assert.Equal(ProtocolVersion.LegacyAes, request.ProtocolVersion);
    }

    [Fact]
    public void MessageDto_DefaultProtocolVersion_IsLegacyAes()
    {
        var dto = new MessageDto(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Alice",
            MessageKind.Text,
            "payload",
            "AES",
            "envelope",
            null,
            null,
            null,
            DateTimeOffset.UtcNow);

        Assert.Equal(ProtocolVersion.LegacyAes, dto.ProtocolVersion);
    }

    [Fact]
    public void SendMessageCommand_DefaultProtocolVersion_IsLegacyAes()
    {
        var command = new SendMessageCommand(
            Guid.NewGuid(),
            Guid.NewGuid(),
            MessageKind.Text,
            "payload",
            "AES",
            "envelope",
            null,
            null,
            null);

        Assert.Equal(ProtocolVersion.LegacyAes, command.ProtocolVersion);
    }
}
