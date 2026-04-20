using Messenger.Application.Abstractions;

namespace Messenger.Tests.Fakes;

/// <summary>
/// Per D-02: configurable ISpkValidator fake. Tests set Result to flip validation outcome:
/// var fake = new FakeSpkValidator { Result = false };
/// </summary>
public sealed class FakeSpkValidator : ISpkValidator
{
    public bool Result { get; set; } = true;

    public bool Validate(byte[] ikPublic, byte[] spkPublic, byte[] spkSignature)
        => Result;
}
