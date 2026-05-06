// Регистрация инфраструктуры BitCanary: БД, JWT, хранилище файлов, сервисы.
using Messenger.Application.Abstractions;
using Messenger.Application.Auth;
using Messenger.Application.Calls;
using Messenger.Application.Chats;
using Messenger.Application.Keys;
using Messenger.Application.Media;
using Messenger.Application.Messages;
using Messenger.Application.Users;
using Messenger.Infrastructure.Authentication;
using Messenger.Infrastructure.Crypto;
using Messenger.Infrastructure.Persistence;
using Messenger.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Messenger.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddMessengerInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));
        services.Configure<StorageOptions>(configuration.GetSection(StorageOptions.SectionName));

        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("Postgres")));

        services.AddScoped<IAppDbContext>(provider => provider.GetRequiredService<AppDbContext>());

        services.AddScoped<SendMessageCommandHandler>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<IChatService, ChatService>();
        services.AddScoped<IMessageService, MessageService>();
        services.AddScoped<IMediaService, MediaService>();
        services.AddScoped<ICallService, CallService>();
        services.AddScoped<IKeyBundleService, KeyBundleService>();

        services.AddSingleton<IPasswordHasher, BcryptPasswordHasher>();
        services.AddSingleton<ISpkValidator, NSecSpkValidator>();
        services.AddSingleton<ITokenService, JwtTokenService>();
        services.AddSingleton<IStorageService, LocalFileStorageService>();

        return services;
    }
}
