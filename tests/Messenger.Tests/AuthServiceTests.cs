// Автотест BitCanary: проверка «AuthServiceTests».
using System.Net;
using FluentAssertions;
using Messenger.Application.Abstractions;
using Messenger.Application.Auth;
using Messenger.Application.Common;
using Messenger.Domain.Entities;
using Messenger.Shared.Contracts.Dtos;
using Messenger.Tests.Fakes;
using NSubstitute;

namespace Messenger.Tests;

public sealed class AuthServiceTests
{
    private static (AuthService sut, IAppDbContext db, IPasswordHasher hasher, ITokenService token) CreateDeps()
    {
        var db = FakeDbContextFactory.Create();
        var hasher = FakePasswordHasher.Create();
        var token = FakeTokenService.Create();
        return (new AuthService(db, hasher, token), db, hasher, token);
    }

    [Fact]
    public async Task Login_ValidCredentials_ReturnsToken()
    {
        var (sut, db, hasher, token) = CreateDeps();
        var user = new User
        {
            UserName = "alice",
            DisplayName = "Alice",
            PasswordHash = "hashed",
            PublicKey = "pk"
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        hasher.Verify(Arg.Any<string>(), Arg.Any<string>()).Returns(true);
        token.CreateAccessToken(Arg.Any<User>()).Returns("test-token");

        var response = await sut.LoginAsync(
            new LoginRequest("alice", "password"),
            CancellationToken.None);

        response.AccessToken.Should().Be("test-token");
        response.UserName.Should().Be("alice");
    }

    [Fact]
    public async Task Register_DuplicateUsername_ThrowsAppException()
    {
        var (sut, db, hasher, _) = CreateDeps();
        var existing = new User
        {
            UserName = "alice",
            DisplayName = "Alice",
            PasswordHash = "hashed",
            PublicKey = "pk"
        };
        db.Users.Add(existing);
        await db.SaveChangesAsync();

        hasher.Hash(Arg.Any<string>()).Returns("hashed");

        Func<Task> act = () => sut.RegisterAsync(
            new RegisterRequest("Alice", "Alice Display", "password", "pk"),
            CancellationToken.None);

        var ex = await act.Should().ThrowAsync<AppException>();
        ex.Which.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }
}
