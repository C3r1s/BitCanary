// Автотест BitCanary: проверка «CorsTests».
using Messenger.Api.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Messenger.Tests;

[Trait("Category", "Integration")]
public sealed class CorsTests : IAsyncLifetime
{
    private TestServer _server = null!;
    private HttpClient _client = null!;

    private static IConfiguration BuildConfig(string[] allowedOrigins)
    {
        var values = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Postgres"] =
                "Host=localhost;Port=5432;Database=test;Username=postgres;Password=postgres",
            ["Jwt:Issuer"] = "TestIssuer",
            ["Jwt:Audience"] = "TestAudience",
            ["Jwt:SigningKey"] = "a-32-character-or-longer-signing-key-value-for-test",
            ["Storage:RootPath"] = "storage"
        };

        for (int i = 0; i < allowedOrigins.Length; i++)
        {
            values[$"Cors:AllowedOrigins:{i}"] = allowedOrigins[i];
        }

        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }

    public async Task InitializeAsync()
    {
        var config = BuildConfig(["http://localhost:5500"]);

        var hostBuilder = new HostBuilder()
            .ConfigureWebHost(webHost =>
            {
                webHost.UseTestServer();
                webHost.ConfigureServices((_, services) =>
                {
                    var allowedOrigins = config
                        .GetSection("Cors:AllowedOrigins")
                        .Get<string[]>() ?? [];

                    services.AddCors(options =>
                    {
                        options.AddDefaultPolicy(policy =>
                        {
                            policy.WithOrigins(allowedOrigins)
                                .AllowAnyMethod()
                                .AllowAnyHeader();
                        });
                    });
                    services.AddRouting();
                });
                webHost.Configure(app =>
                {
                    app.UseCors();
                    app.UseRouting();
                });
            });

        var host = await hostBuilder.StartAsync();
        _server = host.GetTestServer();
        _client = _server.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        _server.Dispose();
        await Task.CompletedTask;
    }

    [Fact]
    public void Cors_ReadsAllowedOriginsFromConfiguration()
    {
        var config = BuildConfig(["http://localhost:5500"]);
        var origins = config.GetSection("Cors:AllowedOrigins").Get<string[]>();

        Assert.NotNull(origins);
        Assert.NotEmpty(origins);
        Assert.Contains("http://localhost:5500", origins);
    }

    [Fact]
    public async Task Cors_ReturnsSpecificOriginHeader_NotWildcard()
    {
        var request = new HttpRequestMessage(HttpMethod.Options, "/");
        request.Headers.Add("Origin", "http://localhost:5500");
        request.Headers.Add("Access-Control-Request-Method", "GET");

        var response = await _client.SendAsync(request);

        Assert.True(
            response.Headers.Contains("Access-Control-Allow-Origin"),
            "Response must contain Access-Control-Allow-Origin header");

        var origin = response.Headers.GetValues("Access-Control-Allow-Origin").FirstOrDefault();

        Assert.NotEqual("*", origin);

        Assert.Equal("http://localhost:5500", origin);
    }

    [Fact]
    public async Task Cors_DoesNotAllowUnknownOrigins()
    {
        var request = new HttpRequestMessage(HttpMethod.Options, "/");
        request.Headers.Add("Origin", "http://evil.example.com");
        request.Headers.Add("Access-Control-Request-Method", "GET");

        var response = await _client.SendAsync(request);

        if (response.Headers.Contains("Access-Control-Allow-Origin"))
        {
            var origin = response.Headers.GetValues("Access-Control-Allow-Origin").FirstOrDefault();
            Assert.NotEqual("*", origin);
            Assert.NotEqual("http://evil.example.com", origin);
        }
    }
}
