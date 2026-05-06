// Регистрация сервисов BitCanary API: аутентификация JWT, лимит логина, инфраструктура.
using System.Text;
using System.Threading.RateLimiting;
using Messenger.Application.Abstractions;
using Messenger.Api.Services;
using Messenger.Infrastructure;
using Messenger.Infrastructure.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;

namespace Messenger.Api.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddMessengerApi(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services.AddHttpContextAccessor();
        services.AddSingleton<ConnectionMappingService>();
        services.AddScoped<ICurrentUserContext, CurrentUserContext>();
        services.AddScoped<IRealtimeNotifier, SignalRRealtimeNotifier>();

        services.AddMessengerInfrastructure(configuration);
        services.AddSignalR();
        services.AddControllers();
        services.AddOpenApi();

        var jwtOptions = configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();

        if ((environment.IsProduction() || environment.IsStaging()) &&
            (string.IsNullOrEmpty(jwtOptions.SigningKey) || jwtOptions.SigningKey.Length < 32))
        {
            throw new InvalidOperationException(
                "Jwt:SigningKey must be set to a value of at least 32 characters in Production and Staging environments.");
        }

        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SigningKey));

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateIssuerSigningKey = true,
                    ValidateLifetime = true,
                    ValidIssuer = jwtOptions.Issuer,
                    ValidAudience = jwtOptions.Audience,
                    IssuerSigningKey = signingKey
                };

                options.Events = new JwtBearerEvents
                {
                    OnMessageReceived = context =>
                    {
                        var accessToken = context.Request.Query["access_token"];
                        var path = context.HttpContext.Request.Path;

                        if (!string.IsNullOrWhiteSpace(accessToken) && path.StartsWithSegments("/hubs/chat"))
                        {
                            context.Token = accessToken;
                        }

                        return Task.CompletedTask;
                    }
                };
            });

        services.AddAuthorization();

        services.AddRateLimiter(options =>
        {
            options.AddPolicy("login", httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 10,
                        Window = TimeSpan.FromSeconds(60),
                        QueueLimit = 0
                    }));
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        });

        return services;
    }
}
