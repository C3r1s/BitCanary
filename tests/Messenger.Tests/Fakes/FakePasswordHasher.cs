// Автотест BitCanary: проверка «FakePasswordHasher».
using Messenger.Application.Abstractions;
using NSubstitute;

namespace Messenger.Tests.Fakes;

public static class FakePasswordHasher
{
    public static IPasswordHasher Create() => Substitute.For<IPasswordHasher>();
}
