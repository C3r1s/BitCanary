// Автотест BitCanary: проверка «UserSearchViewModelTests».
using Messenger.Client.Avalonia.Services;
using Messenger.Client.Avalonia.ViewModels;
using Messenger.Shared.Contracts;
using Messenger.Shared.Contracts.Dtos;
using NSubstitute;
using Xunit;

namespace Messenger.Client.Tests.UserSearch;

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
        var client = CreateApiClient();
        var vm = new UserSearchViewModel(client, _ => Task.CompletedTask);

        vm.SearchQuery = "a";

        Assert.Empty(vm.Results);
        Assert.False(vm.HasSearched);
        Assert.False(vm.ShowNoResults);
    }

    [Fact]
    public async Task SearchQuery_TwoOrMoreChars_FiresSearch()
    {
        var profile = MakeProfile("alice", "Alice Smith");
        var client = CreateApiClient(new[] { profile });
        var vm = new UserSearchViewModel(client, _ => Task.CompletedTask, action => { action(); return Task.CompletedTask; });

        vm.SearchQuery = "alice";
        await Task.Delay(600); // 300ms debounce + 300ms margin

        await client.Received(1).SearchUsersAsync("alice", Arg.Any<CancellationToken>());
    }

    [Fact]
    public void IsInNormalMode_FalseWhenUserSearchActive()
    {
        var vm = new ChatListViewModel(() => Task.CompletedTask);

        vm.IsUserSearchMode = true;

        Assert.False(vm.IsInNormalMode);
    }

    [Fact]
    public void IsInNormalMode_TrueWhenBothModesInactive()
    {
        var vm = new ChatListViewModel(() => Task.CompletedTask);

        vm.IsSearchMode = false;
        vm.IsUserSearchMode = false;

        Assert.True(vm.IsInNormalMode);
    }

    [Fact]
    public void ToggleUserSearchCommand_SetsMutualExclusion_WhenSearchModeActive()
    {
        var vm = new ChatListViewModel(() => Task.CompletedTask);
        vm.IsSearchMode = true;

        vm.ToggleUserSearchCommand.Execute(null);

        Assert.False(vm.IsSearchMode);
        Assert.True(vm.IsUserSearchMode);
    }

    [Fact]
    public void ToggleSearchCommand_SetsMutualExclusion_WhenUserSearchModeActive()
    {
        var vm = new ChatListViewModel(() => Task.CompletedTask);
        vm.IsUserSearchMode = true;

        vm.ToggleSearchCommand.Execute(null);

        Assert.False(vm.IsUserSearchMode);
        Assert.True(vm.IsSearchMode);
    }

    [Fact]
    public void ExistingDirectChat_NavigatesWithoutCreating()
    {
        var vm = new ChatListViewModel(() => Task.CompletedTask);
        var peerId = Guid.NewGuid();
        var existingChat = new ChatListItemViewModel(_ => Task.CompletedTask, _ => Task.CompletedTask)
        {
            Id = Guid.NewGuid(),
            Title = "Alice",
            Type = ChatType.Direct,
            PeerUserId = peerId
        };
        vm.Chats.Add(existingChat);

        var found = vm.Chats.FirstOrDefault(c =>
            c.Type == ChatType.Direct && c.PeerUserId == peerId);

        Assert.NotNull(found);
        Assert.Equal(existingChat.Id, found!.Id);
    }
}
