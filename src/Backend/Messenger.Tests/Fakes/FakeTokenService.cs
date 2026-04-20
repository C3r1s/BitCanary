using Messenger.Application.Abstractions;
using NSubstitute;

namespace Messenger.Tests.Fakes;

/// <summary>
/// Per D-03: NSubstitute stub factory for ITokenService.
/// Usage: var token = FakeTokenService.Create(); token.CreateAccessToken(user).Returns("jwt");
/// </summary>
public static class FakeTokenService
{
    public static ITokenService Create() => Substitute.For<ITokenService>();
}
