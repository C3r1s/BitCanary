using Messenger.Client.Avalonia.ViewModels;
using Messenger.Shared.Contracts;
using Messenger.Shared.Contracts.Dtos;
using Xunit;

namespace Messenger.Client.Tests.GroupInfo;

/// <summary>
/// Unit tests for GroupInfoViewModel and GroupMemberItemViewModel role logic.
/// </summary>
public sealed class GroupInfoViewModelTests
{
    // ── Helpers ────────────────────────────────────────────────────────────────

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

    // ── GroupInfoViewModel.IsAdmin / IsOwner tests ──────────────────────────

    [Fact]
    public async Task LoadAsync_SetsIsAdminTrue_WhenCurrentUserIsAdmin()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var members = new[] { MakeMember(userId, ChatRole.Admin) };
        var summary = MakeSummary(members);
        var vm = MakeVm();

        // Act
        await vm.LoadAsync(summary, userId);

        // Assert
        Assert.True(vm.IsAdmin);
        Assert.False(vm.IsOwner);
    }

    [Fact]
    public async Task LoadAsync_SetsIsOwnerTrue_WhenCurrentUserIsOwner()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var members = new[] { MakeMember(userId, ChatRole.Owner) };
        var summary = MakeSummary(members);
        var vm = MakeVm();

        // Act
        await vm.LoadAsync(summary, userId);

        // Assert
        Assert.True(vm.IsAdmin);   // Owner is also Admin+
        Assert.True(vm.IsOwner);
    }

    [Fact]
    public async Task LoadAsync_SetsIsAdminFalse_WhenCurrentUserIsMember()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var members = new[] { MakeMember(userId, ChatRole.Member) };
        var summary = MakeSummary(members);
        var vm = MakeVm();

        // Act
        await vm.LoadAsync(summary, userId);

        // Assert
        Assert.False(vm.IsAdmin);
        Assert.False(vm.IsOwner);
    }

    // ── GroupMemberItemViewModel.CanLeave tests ──────────────────────────────

    [Fact]
    public void MemberItem_CanLeave_TrueForMember()
    {
        // Arrange — self, Member role
        var item = MakeMemberItem(ChatRole.Member, ChatRole.Member, isSelf: true);

        // Assert
        Assert.True(item.CanLeave);
    }

    [Fact]
    public void MemberItem_CanLeave_FalseForOwner()
    {
        // Arrange — self, Owner role
        var item = MakeMemberItem(ChatRole.Owner, ChatRole.Owner, isSelf: true);

        // Assert
        Assert.False(item.CanLeave);
    }

    // ── GroupMemberItemViewModel.CanRemove tests ─────────────────────────────

    [Fact]
    public void MemberItem_CanRemove_FalseForSelf()
    {
        // Arrange — self targeting self
        var item = MakeMemberItem(ChatRole.Admin, ChatRole.Admin, isSelf: true);

        // Assert
        Assert.False(item.CanRemove);
    }

    [Fact]
    public void MemberItem_CanRemove_FalseForOwnerTarget()
    {
        // Arrange — Admin caller, Owner target (cannot remove Owner)
        var item = MakeMemberItem(ChatRole.Admin, ChatRole.Owner, isSelf: false);

        // Assert
        Assert.False(item.CanRemove);
    }

    [Fact]
    public void MemberItem_CanRemove_FalseWhenAdminTargetsAdmin()
    {
        // Arrange — Admin caller, Admin target
        var item = MakeMemberItem(ChatRole.Admin, ChatRole.Admin, isSelf: false);

        // Assert
        Assert.False(item.CanRemove);
    }

    [Fact]
    public void MemberItem_CanRemove_TrueWhenAdminTargetsMember()
    {
        // Arrange — Admin caller, Member target
        var item = MakeMemberItem(ChatRole.Admin, ChatRole.Member, isSelf: false);

        // Assert
        Assert.True(item.CanRemove);
    }

    // ── GroupMemberItemViewModel.CanMakeAdmin tests ──────────────────────────

    [Fact]
    public void MemberItem_CanMakeAdmin_TrueForMemberWhenCallerIsOwner()
    {
        // Arrange — Owner caller, Member target
        var item = MakeMemberItem(ChatRole.Owner, ChatRole.Member, isSelf: false);

        // Assert
        Assert.True(item.CanMakeAdmin);
    }

    [Fact]
    public void MemberItem_CanMakeAdmin_FalseForAdminWhenCallerIsOwner()
    {
        // Arrange — Owner caller, Admin target (already admin — cannot promote)
        var item = MakeMemberItem(ChatRole.Owner, ChatRole.Admin, isSelf: false);

        // Assert
        Assert.False(item.CanMakeAdmin);
    }

    // ── GroupMemberItemViewModel.CanRevokeAdmin tests ────────────────────────

    [Fact]
    public void MemberItem_CanRevokeAdmin_TrueWhenCallerIsOwnerAndTargetIsAdmin()
    {
        // Arrange — Owner caller, Admin target
        var item = MakeMemberItem(ChatRole.Owner, ChatRole.Admin, isSelf: false);

        // Assert
        Assert.True(item.CanRevokeAdmin);
    }
}
