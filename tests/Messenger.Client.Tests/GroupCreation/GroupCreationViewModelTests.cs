// Автотест BitCanary: проверка «GroupCreationViewModelTests».
using Messenger.Client.Avalonia.ViewModels;
using Messenger.Shared.Contracts;
using Messenger.Shared.Contracts.Dtos;
using NSubstitute;
using Xunit;

namespace Messenger.Client.Tests.GroupCreation;

public sealed class GroupCreationViewModelTests
{
    private static GroupCreationViewModel CreateVm(
        IReadOnlyCollection<UserProfileDto>? searchResults = null,
        Func<CreateChatRequest, Task>? onCreate = null)
    {
        var searchAsync = Substitute.For<Func<string, CancellationToken, Task<IReadOnlyCollection<UserProfileDto>>>>();
        searchAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyCollection<UserProfileDto>>(searchResults ?? Array.Empty<UserProfileDto>()));

        return new GroupCreationViewModel(searchAsync, onCreate ?? (_ => Task.CompletedTask));
    }

    private static UserResultItemViewModel MakeMember(string userName = "alice", string displayName = "Alice") =>
        new UserResultItemViewModel(new UserProfileDto(Guid.NewGuid(), userName, displayName, null, null, null, "pk"));


    [Fact]
    public void CanCreate_FalseWithEmptyName()
    {
        var vm = CreateVm();
        vm.GroupName = string.Empty;
        vm.AddMemberCommand.Execute(MakeMember());

        Assert.False(vm.CanCreate);
    }

    [Fact]
    public void CanCreate_FalseWithNoMembers()
    {
        var vm = CreateVm();
        vm.GroupName = "My Group";

        Assert.False(vm.CanCreate);
    }

    [Fact]
    public void CanCreate_TrueWithNameAndMembers()
    {
        var vm = CreateVm();
        vm.GroupName = "My Group";
        vm.AddMemberCommand.Execute(MakeMember());

        Assert.True(vm.CanCreate);
    }


    [Fact]
    public void AddMember_AppearsInSelectedMembers()
    {
        var vm = CreateVm();
        var member = MakeMember();

        vm.AddMemberCommand.Execute(member);

        Assert.Contains(member, vm.SelectedMembers);
    }

    [Fact]
    public void AddMember_Duplicate_NotAddedTwice()
    {
        var vm = CreateVm();
        var member = MakeMember();

        vm.AddMemberCommand.Execute(member);
        vm.AddMemberCommand.Execute(member);

        Assert.Equal(1, vm.SelectedMembers.Count);
    }

    [Fact]
    public void RemoveMember_RemovedFromSelectedMembers()
    {
        var vm = CreateVm();
        var member = MakeMember();

        vm.AddMemberCommand.Execute(member);
        Assert.Equal(1, vm.SelectedMembers.Count);

        vm.RemoveMemberCommand.Execute(member);
        Assert.Empty(vm.SelectedMembers);
    }


    [Fact]
    public void IsInNormalMode_FalseWhenGroupCreationActive()
    {
        var vm = new ChatListViewModel(() => Task.CompletedTask);

        vm.IsGroupCreationMode = true;

        Assert.False(vm.IsInNormalMode);
    }

    [Fact]
    public void ToggleGroupCreationCommand_MutualExclusion_ClosesUserSearch()
    {
        var vm = new ChatListViewModel(() => Task.CompletedTask);
        vm.IsUserSearchMode = true;

        vm.ToggleGroupCreationCommand.Execute(null);

        Assert.False(vm.IsUserSearchMode);
        Assert.True(vm.IsGroupCreationMode);
    }

    [Fact]
    public void ToggleGroupCreationCommand_MutualExclusion_ClosesSearch()
    {
        var vm = new ChatListViewModel(() => Task.CompletedTask);
        vm.IsSearchMode = true;

        vm.ToggleGroupCreationCommand.Execute(null);

        Assert.False(vm.IsSearchMode);
        Assert.True(vm.IsGroupCreationMode);
    }

    [Fact]
    public void ToggleGroupCreationCommand_Toggle_ClosesWhenAlreadyOpen()
    {
        var vm = new ChatListViewModel(() => Task.CompletedTask);
        vm.IsGroupCreationMode = true;

        vm.ToggleGroupCreationCommand.Execute(null);

        Assert.False(vm.IsGroupCreationMode);
    }
}
