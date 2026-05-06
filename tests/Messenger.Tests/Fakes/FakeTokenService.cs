// Автотест BitCanary: проверка «FakeTokenService».
using Messenger.Application.Abstractions;
using NSubstitute;

namespace Messenger.Tests.Fakes;

public static class FakeTokenService
{
    public static ITokenService Create() => Substitute.For<ITokenService>();
}
