// Автотест BitCanary: проверка «GroupInfoViewModelTests».
using Messenger.Client.Avalonia.ViewModels;
using Messenger.Shared.Contracts;
using Messenger.Shared.Contracts.Dtos;
using Xunit;

namespace Messenger.Client.Tests.GroupInfo;

public sealed class GroupInfoViewModelTests
{

    private static ChatMemberDto MakeMember(Guid userId, ChatRole role, string name = "User") =>
        new(userId, name, null, role, DateTimeOffset.UtcNow);

    private static ChatSummaryDto MakeSummary(IReadOnlyCollection<ChatMemberDto> members, string title = "Test Group") =>
        new(Guid.NewGuid(), title, ChatType.Group, null, null, null, 0, members);

    private static GroupInfoViewModel MakeVm() =>
        new GroupInfoViewModel(
            (_, _) => Task.FromResult(MakeSummary(Array.Empty<ChatMemberDto>())),
            (_, _) => Task.CompletedTask,
            (_, _, _) => Task.CompletedTask,
            (_, _) => Task.FromResult(MakeSummary(Array.Empty<ChatMemberDto>())),
            (_, _) => Task.FromResult<IReadOnlyCollection<UserProfileDto>>(Array.Empty<UserProfileDto>()),
            () => { });

    private static GroupMemberItemViewModel MakeMemberItem(
        ChatRole callerRole,
        ChatRole targetRole,
        bool isSelf) =>
        new GroupMemberItemViewModel(
            callerRole,
            _ => Task.CompletedTask,
            (_, _) => Task.CompletedTask,
            _ => Task.CompletedTask)
        {
            UserId = Guid.NewGuid(),
            DisplayName = "Test User",
            Role = targetRole,
            IsSelf = isSelf
        };


    [Fact]
    public async Task LoadAsync_SetsIsAdminTrue_WhenCurrentUserIsAdmin()
    {
        var userId = Guid.NewGuid();
        var members = new[] { MakeMember(userId, ChatRole.Admin) };
        var summary = MakeSummary(members);
        var vm = MakeVm();

        await vm.LoadAsync(summary, userId);

        Assert.True(vm.IsAdmin);
        Assert.False(vm.IsOwner);
    }

    [Fact]
    public async Task LoadAsync_SetsIsOwnerTrue_WhenCurrentUserIsOwner()
    {
        var userId = Guid.NewGuid();
        var members = new[] { MakeMember(userId, ChatRole.Owner) };
        var summary = MakeSummary(members);
        var vm = MakeVm();

        await vm.LoadAsync(summary, userId);

        Assert.True(vm.IsAdmin);   // Owner is also Admin+
        Assert.True(vm.IsOwner);
    }

    [Fact]
    public async Task LoadAsync_SetsIsAdminFalse_WhenCurrentUserIsMember()
    {
        var userId = Guid.NewGuid();
        var members = new[] { MakeMember(userId, ChatRole.Member) };
        var summary = MakeSummary(members);
        var vm = MakeVm();

        await vm.LoadAsync(summary, userId);

        Assert.False(vm.IsAdmin);
        Assert.False(vm.IsOwner);
    }


    [Fact]
    public void MemberItem_CanLeave_TrueForMember()
    {
        var item = MakeMemberItem(ChatRole.Member, ChatRole.Member, isSelf: true);

        Assert.True(item.CanLeave);
    }

    [Fact]
    public void MemberItem_CanLeave_FalseForOwner()
    {
        var item = MakeMemberItem(ChatRole.Owner, ChatRole.Owner, isSelf: true);

        Assert.False(item.CanLeave);
    }


    [Fact]
    public void MemberItem_CanRemove_FalseForSelf()
    {
        var item = MakeMemberItem(ChatRole.Admin, ChatRole.Admin, isSelf: true);

        Assert.False(item.CanRemove);
    }

    [Fact]
    public void MemberItem_CanRemove_FalseForOwnerTarget()
    {
        var item = MakeMemberItem(ChatRole.Admin, ChatRole.Owner, isSelf: false);

        Assert.False(item.CanRemove);
    }

    [Fact]
    public void MemberItem_CanRemove_FalseWhenAdminTargetsAdmin()
    {
        var item = MakeMemberItem(ChatRole.Admin, ChatRole.Admin, isSelf: false);

        Assert.False(item.CanRemove);
    }

    [Fact]
    public void MemberItem_CanRemove_TrueWhenAdminTargetsMember()
    {
        var item = MakeMemberItem(ChatRole.Admin, ChatRole.Member, isSelf: false);

        Assert.True(item.CanRemove);
    }


    [Fact]
    public void MemberItem_CanMakeAdmin_TrueForMemberWhenCallerIsOwner()
    {
        var item = MakeMemberItem(ChatRole.Owner, ChatRole.Member, isSelf: false);

        Assert.True(item.CanMakeAdmin);
    }

    [Fact]
    public void MemberItem_CanMakeAdmin_FalseForAdminWhenCallerIsOwner()
    {
        var item = MakeMemberItem(ChatRole.Owner, ChatRole.Admin, isSelf: false);

        Assert.False(item.CanMakeAdmin);
    }


    [Fact]
    public void MemberItem_CanRevokeAdmin_TrueWhenCallerIsOwnerAndTargetIsAdmin()
    {
        var item = MakeMemberItem(ChatRole.Owner, ChatRole.Admin, isSelf: false);

        Assert.True(item.CanRevokeAdmin);
    }


    [Fact]
    public async Task RemoveMember_WhenSucceedsAndNotSelf_RemovesMemberFromMembersCollection()
    {
        var currentUserId = Guid.NewGuid();
        var targetUserId = Guid.NewGuid();
        var members = new[]
        {
            MakeMember(currentUserId, ChatRole.Owner, "Owner"),
            MakeMember(targetUserId, ChatRole.Member, "Target"),
        };
        var summary = MakeSummary(members);

        var removeCalls = 0;
        var closeCalls = 0;
        var vm = new GroupInfoViewModel(
            (_, _) => Task.FromResult(MakeSummary(Array.Empty<ChatMemberDto>())),
            (_, _) => { removeCalls++; return Task.CompletedTask; },
            (_, _, _) => Task.CompletedTask,
            (_, _) => Task.FromResult(MakeSummary(Array.Empty<ChatMemberDto>())),
            (_, _) => Task.FromResult<IReadOnlyCollection<UserProfileDto>>(Array.Empty<UserProfileDto>()),
            () => { closeCalls++; });

        await vm.LoadAsync(summary, currentUserId);
        Assert.Equal(2, vm.Members.Count);

        var targetItem = vm.Members.First(m => m.UserId == targetUserId);

        Assert.True(targetItem.RemoveCommand.CanExecute(null));
        await targetItem.RemoveCommand.ExecuteAsync(null);

        Assert.Equal(1, removeCalls);
        Assert.Equal(0, closeCalls);                                    // not self — must NOT close
        Assert.Equal(1, vm.Members.Count);                              // member removed from UI collection
        Assert.DoesNotContain(vm.Members, m => m.UserId == targetUserId);
        Assert.Contains(vm.Members, m => m.UserId == currentUserId);    // caller still present
    }

    [Fact]
    public async Task RemoveMember_WhenSucceedsAndIsSelf_FiresCloseActionAndDoesNotTouchCollection()
    {
        var currentUserId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        var members = new[]
        {
            MakeMember(currentUserId, ChatRole.Member, "Self"),
            MakeMember(otherUserId, ChatRole.Owner, "Other"),
        };
        var summary = MakeSummary(members);

        var closeCalls = 0;
        var vm = new GroupInfoViewModel(
            (_, _) => Task.FromResult(MakeSummary(Array.Empty<ChatMemberDto>())),
            (_, _) => Task.CompletedTask,
            (_, _, _) => Task.CompletedTask,
            (_, _) => Task.FromResult(MakeSummary(Array.Empty<ChatMemberDto>())),
            (_, _) => Task.FromResult<IReadOnlyCollection<UserProfileDto>>(Array.Empty<UserProfileDto>()),
            () => { closeCalls++; });

        await vm.LoadAsync(summary, currentUserId);
        var selfItem = vm.Members.First(m => m.UserId == currentUserId);

        Assert.True(selfItem.LeaveCommand.CanExecute(null));
        await selfItem.LeaveCommand.ExecuteAsync(null);

        Assert.Equal(1, closeCalls);
        Assert.Equal(2, vm.Members.Count);
        Assert.Contains(vm.Members, m => m.UserId == currentUserId);
    }
}
