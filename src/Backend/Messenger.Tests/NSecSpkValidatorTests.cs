using FluentAssertions;
using Messenger.Infrastructure.Crypto;
using Messenger.Tests.Fakes;

namespace Messenger.Tests;

public sealed class NSecSpkValidatorTests
{
    private readonly NSecSpkValidator _sut = new();

    [Fact]
    public void Validate_ValidSpk_ReturnsTrue()
    {
        // Arrange — TestKeys generates a real Ed25519-signed bundle
        var (ikPublic, spkPublic, spkSignature) = TestKeys.GenerateSignedSpkBundle();

        // Act
        var result = _sut.Validate(ikPublic, spkPublic, spkSignature);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void Validate_FlippedByte_ReturnsFalse()
    {
        // Arrange — generate valid bundle, then corrupt exactly one byte of the signature
        var (ikPublic, spkPublic, spkSignature) = TestKeys.GenerateSignedSpkBundle();
        spkSignature[0] ^= 0xFF;

        // Act
        var result = _sut.Validate(ikPublic, spkPublic, spkSignature);

        // Assert
        result.Should().BeFalse();
    }
}
