// Регистрация и вход: проверка пароля (BCrypt) и выпуск JWT-токена.
using System.Net;
using Messenger.Application.Abstractions;
using Messenger.Application.Common;
using Messenger.Domain.Entities;
using Messenger.Shared.Contracts.Dtos;
using Microsoft.EntityFrameworkCore;

namespace Messenger.Application.Auth;

public sealed class AuthService(
    IAppDbContext dbContext,
    IPasswordHasher passwordHasher,
    ITokenService tokenService) : IAuthService
{
    public async Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken)
    {
        var normalizedUserName = request.UserName.Trim().ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(normalizedUserName) || string.IsNullOrWhiteSpace(request.Password))
        {
            throw new AppException("Username and password are required.");
        }

        var exists = await dbContext.Users.AnyAsync(x => x.UserName == normalizedUserName, cancellationToken);
        if (exists)
        {
            throw new AppException("Username is already taken.", HttpStatusCode.Conflict);
        }

        var user = new User
        {
            UserName = normalizedUserName,
            DisplayName = string.IsNullOrWhiteSpace(request.DisplayName) ? request.UserName.Trim() : request.DisplayName.Trim(),
            PasswordHash = passwordHasher.Hash(request.Password),
            PublicKey = request.PublicKey.Trim()
        };

        var settings = new UserSettings
        {
            User = user,
            UserId = user.Id
        };

        dbContext.Users.Add(user);
        dbContext.UserSettings.Add(settings);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new AuthResponse(user.Id, user.UserName, user.DisplayName, tokenService.CreateAccessToken(user));
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        var normalizedUserName = request.UserName.Trim().ToLowerInvariant();

        var user = await dbContext.Users.SingleOrDefaultAsync(x => x.UserName == normalizedUserName, cancellationToken);
        if (user is null || !passwordHasher.Verify(request.Password, user.PasswordHash))
        {
            throw new AppException("Invalid username or password.", HttpStatusCode.Unauthorized);
        }

        return new AuthResponse(user.Id, user.UserName, user.DisplayName, tokenService.CreateAccessToken(user));
    }
}
