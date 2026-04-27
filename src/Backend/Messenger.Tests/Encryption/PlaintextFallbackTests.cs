using FluentAssertions;
using Messenger.Client.Avalonia.Services;
using Messenger.Client.Avalonia.Services.Crypto;
using Messenger.Shared.Contracts;
using Messenger.Shared.Contracts.Dtos;
using NSubstitute;

namespace Messenger.Tests.Encryption;

/// <summary>
/// Unit tests for FEA-03: DecryptAsync plaintext branch in SignalProtocolEncryptionService.
/// </summary>
[Trait("req", "FEA03")]
public sealed class PlaintextFallbackTests
{
    private static SignalProtocolEncryptionService BuildSut(ILocalMessageRepository localMessageRepository)
    {
        // Only localMessageRepository is called in the plaintext branch.
        // All other dependencies are stubs that are never invoked during this path.
        var x3dh = Substitute.For<IX3DHService>();
        var sessionManager = Substitute.For<ISessionManager>();
        var apiClient = Substitute.For<IMessengerApiClient>();
        var sessionService = Substitute.For<IClientSessionService>();
        var sessionRepository = Substitute.For<IRatchetSessionRepository>();
        var changeDetector = Substitute.For<IIdentityKeyChangeDetector>();

        // Concrete types — safe to pass null because the plaintext branch returns
        // before either of these instances are invoked.
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
            localMessageRepository);
    }

    [Fact]
    public async Task FEA03_Decrypt_Plaintext_ReturnsPayloadDirectly()
    {
        // Arrange
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

        // Act
        var result = await sut.DecryptAsync(message);

        // Assert
        result.Should().Be("hello world");
    }

    [Fact]
    public async Task FEA03_Decrypt_Plaintext_PersistsToLocalRepo()
    {
        // Arrange
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

        // Act
        await sut.DecryptAsync(message);

        // Assert — both repo calls must occur for FTS5 indexing
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
