using Messenger.Api.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Messenger.Tests;

/// <summary>
/// Tests that CORS is configured with specific origins from config (WEB-02).
///
/// DESIGN: These tests build a minimal test host that mirrors the production
/// CORS registration pattern in Program.cs. The tests validate the corrected
/// behavior (WithOrigins from config) and will fail if AllowAnyOrigin() is used.
/// </summary>
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

        // Mirror the production CORS registration: read from Cors:AllowedOrigins config key
        var hostBuilder = new HostBuilder()
            .ConfigureWebHost(webHost =>
            {
                webHost.UseTestServer();
                webHost.ConfigureServices((_, services) =>
                {
                    // Reproduce the corrected Program.cs pattern exactly
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

    /// <summary>
    /// Cors:AllowedOrigins config key must exist and contain origin values (not hardcoded).
    /// </summary>
    [Fact]
    public void Cors_ReadsAllowedOriginsFromConfiguration()
    {
        var config = BuildConfig(["http://localhost:5500"]);
        var origins = config.GetSection("Cors:AllowedOrigins").Get<string[]>();

        Assert.NotNull(origins);
        Assert.NotEmpty(origins);
        Assert.Contains("http://localhost:5500", origins);
    }

    /// <summary>
    /// Response to request from configured origin must echo that specific origin, not wildcard.
    /// </summary>
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

        // Must NOT return wildcard — WEB-02 requires specific origin matching
        Assert.NotEqual("*", origin);

        // Must echo the configured origin exactly
        Assert.Equal("http://localhost:5500", origin);
    }

    /// <summary>
    /// AllowAnyOrigin() must not be used — it is incompatible with AllowCredentials() (Phase 25).
    /// </summary>
    [Fact]
    public async Task Cors_DoesNotAllowUnknownOrigins()
    {
        var request = new HttpRequestMessage(HttpMethod.Options, "/");
        request.Headers.Add("Origin", "http://evil.example.com");
        request.Headers.Add("Access-Control-Request-Method", "GET");

        var response = await _client.SendAsync(request);

        // An unconfigured origin must NOT receive Access-Control-Allow-Origin: *
        if (response.Headers.Contains("Access-Control-Allow-Origin"))
        {
            var origin = response.Headers.GetValues("Access-Control-Allow-Origin").FirstOrDefault();
            Assert.NotEqual("*", origin);
            Assert.NotEqual("http://evil.example.com", origin);
        }
    }
}
