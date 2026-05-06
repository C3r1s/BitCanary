// Автотест BitCanary: проверка «RateLimiterTests».
using System.Net;
using System.Net.Http.Json;
using Messenger.Api.Controllers;
using Messenger.Api.Extensions;
using Messenger.Application.Abstractions;
using Messenger.Shared.Contracts.Dtos;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;

namespace Messenger.Tests;

[Trait("Category", "Integration")]
public sealed class RateLimiterTests
{
    private static IHost BuildTestHost()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] =
                    "Host=localhost;Port=5432;Database=test;Username=postgres;Password=postgres",
                ["Jwt:Issuer"] = "TestIssuer",
                ["Jwt:Audience"] = "TestAudience",
                ["Jwt:SigningKey"] = "a-32-character-or-longer-signing-key-value",
                ["Storage:RootPath"] = "storage"
            })
            .Build();

        var host = new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.UseEnvironment("Development");

                webBuilder.ConfigureServices((ctx, services) =>
                {
                    services.AddMessengerApi(config, ctx.HostingEnvironment);

                    var stub = Substitute.For<IAuthService>();
                    var fakeResponse = new AuthResponse(
                        Guid.NewGuid(),
                        "testuser",
                        "Test User",
                        "test-access-token");
                    stub.LoginAsync(Arg.Any<LoginRequest>(), Arg.Any<CancellationToken>())
                        .Returns(fakeResponse);
                    stub.RegisterAsync(Arg.Any<RegisterRequest>(), Arg.Any<CancellationToken>())
                        .Returns(fakeResponse);
                    services.AddSingleton(stub);

                    services.AddControllers()
                        .AddApplicationPart(typeof(AuthController).Assembly);
                });

                webBuilder.Configure(app =>
                {
                    app.UseRouting();
                    app.UseRateLimiter();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints => endpoints.MapControllers());
                });
            })
            .Build();

        host.Start();
        return host;
    }

    [Fact]
    public async Task FirstTenLoginRequests_DoNotReturnTooManyRequests()
    {
        using var host = BuildTestHost();
        var client = host.GetTestClient();

        for (int i = 1; i <= 10; i++)
        {
            var response = await client.PostAsJsonAsync(
                "/api/auth/login",
                new { userName = "alice", password = "password" });

            Assert.NotEqual(
                HttpStatusCode.TooManyRequests,
                response.StatusCode);
        }
    }

    [Fact]
    public async Task RequestNumber11_ReturnsTooManyRequests()
    {
        using var host = BuildTestHost();
        var client = host.GetTestClient();

        HttpResponseMessage? lastResponse = null;

        for (int i = 1; i <= 11; i++)
        {
            lastResponse = await client.PostAsJsonAsync(
                "/api/auth/login",
                new { userName = "alice", password = "password" });
        }

        Assert.NotNull(lastResponse);
        Assert.Equal(HttpStatusCode.TooManyRequests, lastResponse!.StatusCode);
    }
}
