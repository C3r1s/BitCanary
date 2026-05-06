// Автотест BitCanary: проверка «PlaintextFallbackTests».
using FluentAssertions;
using Messenger.Client.Avalonia.Services;
using Messenger.Client.Avalonia.Services.Crypto;
using Messenger.Shared.Contracts;
using Messenger.Shared.Contracts.Dtos;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Messenger.Tests.Encryption;

[Trait("req", "FEA03")]
public sealed class PlaintextFallbackTests
{
    private static SignalProtocolEncryptionService BuildSut(ILocalMessageRepository localMessageRepository)
    {
        var x3dh = Substitute.For<IX3DHService>();
        var sessionManager = Substitute.For<ISessionManager>();
        var apiClient = Substitute.For<IMessengerApiClient>();
        var sessionService = Substitute.For<IClientSessionService>();
        var sessionRepository = Substitute.For<IRatchetSessionRepository>();
        var changeDetector = Substitute.For<IIdentityKeyChangeDetector>();
        var logger = Substitute.For<ILogger<SignalProtocolEncryptionService>>();

        KeyPublicationService keyPublication = null!;
        LocalEnvelopeEncryptionService legacyService = null!;

        return new SignalProtocolEncryptionService(
            x3dh,
            sessionManager,
            keyPublication,
            apiClient,
            sessionService,
            legacyService,
            sessionRepository,
            changeDetector,
            localMessageRepository,
            logger);
    }

    [Fact]
    public async Task FEA03_Decrypt_Plaintext_ReturnsPayloadDirectly()
    {
        var localRepo = Substitute.For<ILocalMessageRepository>();
        var sut = BuildSut(localRepo);

        var message = new MessageDto(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "alice",
            MessageKind.Text,
            "hello world",
            "plaintext",
            string.Empty,
            null,
            null,
            null,
            DateTimeOffset.UtcNow,
            ProtocolVersion.Plaintext);

        var result = await sut.DecryptAsync(message);

        result.Should().Be("hello world");
    }

    [Fact]
    public async Task FEA03_Decrypt_Plaintext_PersistsToLocalRepo()
    {
        var localRepo = Substitute.For<ILocalMessageRepository>();
        var sut = BuildSut(localRepo);

        var message = new MessageDto(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "alice",
            MessageKind.Text,
            "hello world",
            "plaintext",
            string.Empty,
            null,
            null,
            null,
            DateTimeOffset.UtcNow,
            ProtocolVersion.Plaintext);

        await sut.DecryptAsync(message);

        await localRepo.Received(1).SaveMessageAsync(
            message,
            (int)ProtocolVersion.Plaintext,
            Arg.Any<CancellationToken>());

        await localRepo.Received(1).UpdatePlaintextBodyAsync(
            message.Id,
            "hello world",
            Arg.Any<CancellationToken>());
    }
}
