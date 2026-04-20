using FluentAssertions;
using Messenger.Tests.Fakes;

namespace Messenger.Tests;

/// <summary>
/// Smoke test — proves dotnet test exits 0 and that Fakes/ types resolve.
/// Delete this file once phase 18 adds real AuthServiceTests / KeyBundleServiceTests.
/// </summary>
public sealed class PlaceholderTest
{
    [Fact]
    public void Scaffold_IsAlive()
    {
        var fake = new FakeSpkValidator { Result = true };
        fake.Validate(new byte[32], new byte[32], new byte[64]).Should().BeTrue();
    }
}
