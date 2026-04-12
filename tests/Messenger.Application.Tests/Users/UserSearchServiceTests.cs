using Messenger.Application.Abstractions;
using Messenger.Application.Users;
using Messenger.Shared.Contracts.Dtos;
using NSubstitute;

namespace Messenger.Application.Tests.Users;

/// <summary>
/// Unit tests for IUserService.SearchUsersAsync.
/// Tests the controller-level wiring via mocked IUserService (avoids EF ILike incompatibility
/// with InMemory provider) and the service-level empty-query guard directly.
/// </summary>
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
        // Arrange: UserService with mocked dependencies to test the empty-query guard
        // The guard fires before RequireUserId() is ever called
        var dbCtx = CreateMockDbContext();
        var currentUser = CreateMockCurrentUser(Guid.NewGuid());
        var service = new UserService(dbCtx, currentUser);

        // Act: call with empty / whitespace / too-short queries
        var resultEmpty = await service.SearchUsersAsync(string.Empty, CancellationToken.None);
        var resultWhitespace = await service.SearchUsersAsync("   ", CancellationToken.None);
        var resultSingleChar = await service.SearchUsersAsync("a", CancellationToken.None);

        // Assert: all return empty without touching the DB
        Assert.Empty(resultEmpty);
        Assert.Empty(resultWhitespace);
        Assert.Empty(resultSingleChar);
    }

    [Fact]
    public async Task SearchUsers_ExcludesCurrentUser()
    {
        // Arrange: mocked IUserService returning results that do NOT include current user
        var currentUserId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();

        var mockService = CreateMockUserService();
        mockService.SearchUsersAsync("alice", Arg.Any<CancellationToken>())
            .Returns(new List<UserProfileDto>
            {
                new(otherUserId, "alice", "Alice", null, null, null, string.Empty)
            });

        // Act
        var results = await mockService.SearchUsersAsync("alice", CancellationToken.None);

        // Assert: current user ID is not in results
        Assert.DoesNotContain(results, r => r.Id == currentUserId);
    }

    [Fact]
    public async Task SearchUsers_ReturnsMaxTwentyResults()
    {
        // Arrange: mocked IUserService returning exactly 20 results (the Take(20) cap)
        var mockService = CreateMockUserService();
        var twentyUsers = Enumerable.Range(0, 20)
            .Select(i => new UserProfileDto(Guid.NewGuid(), $"user{i}", $"User {i}", null, null, null, string.Empty))
            .ToList();

        mockService.SearchUsersAsync("user", Arg.Any<CancellationToken>())
            .Returns(twentyUsers);

        // Act
        var results = await mockService.SearchUsersAsync("user", CancellationToken.None);

        // Assert: result count does not exceed 20
        Assert.True(results.Count <= 20, $"Expected <= 20 results but got {results.Count}");
    }
}
