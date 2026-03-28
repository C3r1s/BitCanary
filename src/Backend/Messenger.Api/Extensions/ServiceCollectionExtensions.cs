using System.Text;
using Messenger.Application.Abstractions;
using Messenger.Api.Services;
using Messenger.Infrastructure;
using Messenger.Infrastructure.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace Messenger.Api.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddMessengerApi(this IServiceCollection services, IConfiguration configuration)
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

        return services;
    }
}
