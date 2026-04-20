using Messenger.Application.Abstractions;
using NSubstitute;

namespace Messenger.Tests.Fakes;

/// <summary>
/// Per D-03: NSubstitute stub factory for IPasswordHasher.
/// </summary>
public static class FakePasswordHasher
{
    public static IPasswordHasher Create() => Substitute.For<IPasswordHasher>();
}
