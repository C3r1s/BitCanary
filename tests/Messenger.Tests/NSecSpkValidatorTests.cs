// Автотест BitCanary: проверка «NSecSpkValidatorTests».
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
        var (ikPublic, spkPublic, spkSignature) = TestKeys.GenerateSignedSpkBundle();

        var result = _sut.Validate(ikPublic, spkPublic, spkSignature);

        result.Should().BeTrue();
    }

    [Fact]
    public void Validate_FlippedByte_ReturnsFalse()
    {
        var (ikPublic, spkPublic, spkSignature) = TestKeys.GenerateSignedSpkBundle();
        spkSignature[0] ^= 0xFF;

        var result = _sut.Validate(ikPublic, spkPublic, spkSignature);

        result.Should().BeFalse();
    }
}
