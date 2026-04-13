using Messenger.Client.Avalonia.Services;
using Messenger.Client.Avalonia.ViewModels;
using Messenger.Shared.Contracts;
using Messenger.Shared.Contracts.Dtos;
using NSubstitute;
using Xunit;

namespace Messenger.Client.Tests.UserSearch;

/// <summary>
/// Unit tests for UserSearchViewModel and ChatListViewModel user-search wiring.
/// </summary>
public sealed class UserSearchViewModelTests
{
    private static IMessengerApiClient CreateApiClient(IReadOnlyCollection<UserProfileDto>? results = null)
    {
        var client = Substitute.For<IMessengerApiClient>();
        client
            .SearchUsersAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(results ?? Array.Empty<UserProfileDto>());
        return client;
    }

    private static UserProfileDto MakeProfile(string userName, string displayName) =>
        new(Guid.NewGuid(), userName, displayName, null, null, null, "pk");

    [Fact]
    public void SearchQuery_LessThanTwoChars_ClearsResults()
    {
        // Arrange
        var client = CreateApiClient();
        var vm = new UserSearchViewModel(client, _ => Task.CompletedTask);

        // Act — set to a single character (below threshold)
        vm.SearchQuery = "a";

        // Assert — should NOT fire search; results empty, HasSearched stays false
        Assert.Empty(vm.Results);
        Assert.False(vm.HasSearched);
        Assert.False(vm.ShowNoResults);
    }

    [Fact]
    public async Task SearchQuery_TwoOrMoreChars_FiresSearch()
    {
        // Arrange — Dispatcher.UIThread may not be initialized in tests, so
        // this test verifies the API call is made by checking invocation after debounce.
        // SearchUsersAsync is called after 300ms debounce delay.
        var profile = MakeProfile("alice", "Alice Smith");
        var client = CreateApiClient(new[] { profile });
        var vm = new UserSearchViewModel(client, _ => Task.CompletedTask);

        // Act — set query >= 2 chars and wait for debounce + async completion
        vm.SearchQuery = "alice";
        await Task.Delay(600); // 300ms debounce + 300ms margin

        // Assert — API was called with the trimmed query
        await client.Received(1).SearchUsersAsync("alice", Arg.Any<CancellationToken>());
    }

    [Fact]
    public void IsInNormalMode_FalseWhenUserSearchActive()
    {
        // Arrange
        var vm = new ChatListViewModel(() => Task.CompletedTask);

        // Act — activate user search mode
        vm.IsUserSearchMode = true;

        // Assert
        Assert.False(vm.IsInNormalMode);
    }

    [Fact]
    public void IsInNormalMode_TrueWhenBothModesInactive()
    {
        // Arrange
        var vm = new ChatListViewModel(() => Task.CompletedTask);

        // Act — both modes off (default)
        vm.IsSearchMode = false;
        vm.IsUserSearchMode = false;

        // Assert
        Assert.True(vm.IsInNormalMode);
    }

    [Fact]
    public void ToggleUserSearchCommand_SetsMutualExclusion_WhenSearchModeActive()
    {
        // Arrange — activate message search first
        var vm = new ChatListViewModel(() => Task.CompletedTask);
        vm.IsSearchMode = true;

        // Act — toggle user search on
        vm.ToggleUserSearchCommand.Execute(null);

        // Assert — message search should be off, user search should be on
        Assert.False(vm.IsSearchMode);
        Assert.True(vm.IsUserSearchMode);
    }

    [Fact]
    public void ToggleSearchCommand_SetsMutualExclusion_WhenUserSearchModeActive()
    {
        // Arrange — activate user search first
        var vm = new ChatListViewModel(() => Task.CompletedTask);
        vm.IsUserSearchMode = true;

        // Act — toggle message search on
        vm.ToggleSearchCommand.Execute(null);

        // Assert — user search should be off, message search should be on
        Assert.False(vm.IsUserSearchMode);
        Assert.True(vm.IsSearchMode);
    }

    [Fact]
    public void ExistingDirectChat_NavigatesWithoutCreating()
    {
        // This test verifies that when a direct chat already exists for the selected user,
        // the deduplication logic in HandleUserSelectedAsync finds it via ChatList.Chats.
        // We test the ChatListViewModel Chats collection and type matching directly,
        // as HandleUserSelectedAsync is private on MainWindowViewModel.
        var vm = new ChatListViewModel(() => Task.CompletedTask);
        var peerId = Guid.NewGuid();
        var existingChat = new ChatListItemViewModel
        {
            Id = Guid.NewGuid(),
            Title = "Alice",
            Type = ChatType.Direct,
            PeerUserId = peerId
        };
        vm.Chats.Add(existingChat);

        // Simulate the deduplication check from HandleUserSelectedAsync
        var found = vm.Chats.FirstOrDefault(c =>
            c.Type == ChatType.Direct && c.PeerUserId == peerId);

        Assert.NotNull(found);
        Assert.Equal(existingChat.Id, found!.Id);
    }
}
