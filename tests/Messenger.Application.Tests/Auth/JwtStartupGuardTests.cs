using Messenger.Api.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;

namespace Messenger.Application.Tests.Auth;

[Trait("Category", "Unit")]
public sealed class JwtStartupGuardTests
{
    private static IHostEnvironment CreateEnvironment(string environmentName)
    {
        var env = Substitute.For<IHostEnvironment>();
        env.EnvironmentName.Returns(environmentName);
        return env;
    }

    private static IConfiguration BuildConfig(string? signingKey = null)
    {
        var values = new Dictionary<string, string?>
        {
            // Minimal values to prevent AddMessengerInfrastructure from crashing on missing config
            ["ConnectionStrings:Postgres"] = "Host=localhost;Port=5432;Database=test;Username=postgres;Password=postgres",
            ["Jwt:Issuer"] = "TestIssuer",
            ["Jwt:Audience"] = "TestAudience",
            ["Storage:RootPath"] = "storage"
        };
        if (signingKey is not null)
            values["Jwt:SigningKey"] = signingKey;

        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }

    [Fact]
    public void Guard_ThrowsInProduction_WhenSigningKeyIsEmpty()
    {
        var services = new ServiceCollection();
        var env = CreateEnvironment("Production");
        var config = BuildConfig(signingKey: null);  // No key set — defaults to string.Empty

        var ex = Assert.Throws<InvalidOperationException>(() =>
            services.AddMessengerApi(config, env));

        Assert.Contains(
            "Jwt:SigningKey must be set to a value of at least 32 characters in Production and Staging environments.",
            ex.Message);
    }

    [Fact]
    public void Guard_ThrowsInProduction_WhenSigningKeyIsTooShort()
    {
        var services = new ServiceCollection();
        var env = CreateEnvironment("Production");
        var config = BuildConfig(signingKey: "short");

        var ex = Assert.Throws<InvalidOperationException>(() =>
            services.AddMessengerApi(config, env));

        Assert.Contains(
            "Jwt:SigningKey must be set to a value of at least 32 characters in Production and Staging environments.",
            ex.Message);
    }

    [Fact]
    public void Guard_DoesNotThrowInDevelopment_WhenSigningKeyIsEmpty()
    {
        var services = new ServiceCollection();
        var env = CreateEnvironment("Development");
        // Short key (< 32 chars) — guard is NOT active in Development; SymmetricSecurityKey requires non-empty bytes
        var config = BuildConfig(signingKey: "dev-short-key");

        // Should not throw; AddMessengerApi returns services normally
        var exception = Record.Exception(() => services.AddMessengerApi(config, env));
        Assert.Null(exception);
    }

    [Fact]
    public void Guard_DoesNotThrowInProduction_WhenSigningKeyIsLongEnough()
    {
        var services = new ServiceCollection();
        var env = CreateEnvironment("Production");
        // 42-char key — exceeds the 32-char minimum
        var config = BuildConfig(signingKey: "a-32-character-or-longer-signing-key-value");

        var exception = Record.Exception(() => services.AddMessengerApi(config, env));
        Assert.Null(exception);
    }
}
