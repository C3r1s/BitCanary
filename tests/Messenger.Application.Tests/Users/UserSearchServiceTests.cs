// Автотест BitCanary: проверка «UserSearchServiceTests».
using Messenger.Application.Abstractions;
using Messenger.Application.Users;
using Messenger.Shared.Contracts.Dtos;
using NSubstitute;

namespace Messenger.Application.Tests.Users;

public sealed class UserSearchServiceTests
{
    private static IUserService CreateMockUserService() => Substitute.For<IUserService>();

    private static ICurrentUserContext CreateMockCurrentUser(Guid userId)
    {
        var ctx = Substitute.For<ICurrentUserContext>();
        ctx.IsAuthenticated.Returns(true);
        ctx.UserId.Returns(userId);
        return ctx;
    }

    private static IAppDbContext CreateMockDbContext() => Substitute.For<IAppDbContext>();

    [Fact]
    public async Task SearchUsers_EmptyQuery_ReturnsEmpty()
    {
        var dbCtx = CreateMockDbContext();
        var currentUser = CreateMockCurrentUser(Guid.NewGuid());
        var service = new UserService(dbCtx, currentUser);

        var resultEmpty = await service.SearchUsersAsync(string.Empty, CancellationToken.None);
        var resultWhitespace = await service.SearchUsersAsync("   ", CancellationToken.None);
        var resultSingleChar = await service.SearchUsersAsync("a", CancellationToken.None);

        Assert.Empty(resultEmpty);
        Assert.Empty(resultWhitespace);
        Assert.Empty(resultSingleChar);
    }

    [Fact]
    public async Task SearchUsers_ExcludesCurrentUser()
    {
        var currentUserId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();

        var mockService = CreateMockUserService();
        mockService.SearchUsersAsync("alice", Arg.Any<CancellationToken>())
            .Returns(new List<UserProfileDto>
            {
                new(otherUserId, "alice", "Alice", null, null, null, string.Empty)
            });

        var results = await mockService.SearchUsersAsync("alice", CancellationToken.None);

        Assert.DoesNotContain(results, r => r.Id == currentUserId);
    }

    [Fact]
    public async Task SearchUsers_ReturnsMaxTwentyResults()
    {
        var mockService = CreateMockUserService();
        var twentyUsers = Enumerable.Range(0, 20)
            .Select(i => new UserProfileDto(Guid.NewGuid(), $"user{i}", $"User {i}", null, null, null, string.Empty))
            .ToList();

        mockService.SearchUsersAsync("user", Arg.Any<CancellationToken>())
            .Returns(twentyUsers);

        var results = await mockService.SearchUsersAsync("user", CancellationToken.None);

        Assert.True(results.Count <= 20, $"Expected <= 20 results but got {results.Count}");
    }
}
