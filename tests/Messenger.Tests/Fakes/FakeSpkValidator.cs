// Автотест BitCanary: проверка «FakeSpkValidator».
using Messenger.Application.Abstractions;

namespace Messenger.Tests.Fakes;

public sealed class FakeSpkValidator : ISpkValidator
{
    public bool Result { get; set; } = true;

    public bool Validate(byte[] ikPublic, byte[] spkPublic, byte[] spkSignature)
        => Result;
}
