using Messenger.Client.Avalonia.ViewModels;
using Messenger.Shared.Contracts;
using Messenger.Shared.Contracts.Dtos;
using NSubstitute;
using Xunit;

namespace Messenger.Client.Tests.GroupCreation;

/// <summary>
/// Unit tests for GroupCreationViewModel and the fourth mutual-exclusion mode on ChatListViewModel.
/// </summary>
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

    // -------------------------------------------------------------------------
    // CanCreate tests
    // -------------------------------------------------------------------------

    [Fact]
    public void CanCreate_FalseWithEmptyName()
    {
        // GroupName="" and SelectedMembers has 1 entry → CanCreate is false
        var vm = CreateVm();
        vm.GroupName = string.Empty;
        vm.AddMemberCommand.Execute(MakeMember());

        Assert.False(vm.CanCreate);
    }

    [Fact]
    public void CanCreate_FalseWithNoMembers()
    {
        // GroupName="My Group" and SelectedMembers is empty → CanCreate is false
        var vm = CreateVm();
        vm.GroupName = "My Group";

        Assert.False(vm.CanCreate);
    }

    [Fact]
    public void CanCreate_TrueWithNameAndMembers()
    {
        // GroupName="My Group" and SelectedMembers has 1 entry → CanCreate is true
        var vm = CreateVm();
        vm.GroupName = "My Group";
        vm.AddMemberCommand.Execute(MakeMember());

        Assert.True(vm.CanCreate);
    }

    // -------------------------------------------------------------------------
    // Member management tests
    // -------------------------------------------------------------------------

    [Fact]
    public void AddMember_AppearsInSelectedMembers()
    {
        // AddMemberCommand with a UserResultItemViewModel → item in SelectedMembers
        var vm = CreateVm();
        var member = MakeMember();

        vm.AddMemberCommand.Execute(member);

        Assert.Contains(member, vm.SelectedMembers);
    }

    [Fact]
    public void AddMember_Duplicate_NotAddedTwice()
    {
        // AddMemberCommand same UserId twice → SelectedMembers.Count == 1
        var vm = CreateVm();
        var member = MakeMember();

        vm.AddMemberCommand.Execute(member);
        vm.AddMemberCommand.Execute(member);

        Assert.Equal(1, vm.SelectedMembers.Count);
    }

    [Fact]
    public void RemoveMember_RemovedFromSelectedMembers()
    {
        // AddMemberCommand then RemoveMemberCommand → SelectedMembers.Count == 0
        var vm = CreateVm();
        var member = MakeMember();

        vm.AddMemberCommand.Execute(member);
        Assert.Equal(1, vm.SelectedMembers.Count);

        vm.RemoveMemberCommand.Execute(member);
        Assert.Empty(vm.SelectedMembers);
    }

    // -------------------------------------------------------------------------
    // ChatListViewModel mutual-exclusion tests
    // -------------------------------------------------------------------------

    [Fact]
    public void IsInNormalMode_FalseWhenGroupCreationActive()
    {
        // set IsGroupCreationMode=true → IsInNormalMode is false
        var vm = new ChatListViewModel(() => Task.CompletedTask);

        vm.IsGroupCreationMode = true;

        Assert.False(vm.IsInNormalMode);
    }

    [Fact]
    public void ToggleGroupCreationCommand_MutualExclusion_ClosesUserSearch()
    {
        // IsUserSearchMode=true, execute ToggleGroupCreationCommand → IsUserSearchMode=false, IsGroupCreationMode=true
        var vm = new ChatListViewModel(() => Task.CompletedTask);
        vm.IsUserSearchMode = true;

        vm.ToggleGroupCreationCommand.Execute(null);

        Assert.False(vm.IsUserSearchMode);
        Assert.True(vm.IsGroupCreationMode);
    }

    [Fact]
    public void ToggleGroupCreationCommand_MutualExclusion_ClosesSearch()
    {
        // IsSearchMode=true, execute ToggleGroupCreationCommand → IsSearchMode=false, IsGroupCreationMode=true
        var vm = new ChatListViewModel(() => Task.CompletedTask);
        vm.IsSearchMode = true;

        vm.ToggleGroupCreationCommand.Execute(null);

        Assert.False(vm.IsSearchMode);
        Assert.True(vm.IsGroupCreationMode);
    }

    [Fact]
    public void ToggleGroupCreationCommand_Toggle_ClosesWhenAlreadyOpen()
    {
        // IsGroupCreationMode=true, execute ToggleGroupCreationCommand → IsGroupCreationMode=false
        var vm = new ChatListViewModel(() => Task.CompletedTask);
        vm.IsGroupCreationMode = true;

        vm.ToggleGroupCreationCommand.Execute(null);

        Assert.False(vm.IsGroupCreationMode);
    }
}
