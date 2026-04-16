using CommunityToolkit.Mvvm.Input;
using Messenger.Shared.Contracts;

namespace Messenger.Client.Avalonia.ViewModels;

public sealed class GroupMemberItemViewModel : ViewModelBase
{
    public Guid UserId { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public ChatRole Role { get; init; }
    public bool IsSelf { get; init; }

    // callerRole is the current user's role in this chat
    private readonly ChatRole _callerRole;

    public string RoleBadge => Role switch
    {
        ChatRole.Owner => "[ OWNER ]",
        ChatRole.Admin => "[ ADMIN ]",
        _ => "[ MEMBER ]"
    };

    // CanLeave: any non-Owner member can leave (Owner cannot leave without transfer/delete)
    public bool CanLeave => IsSelf && Role != ChatRole.Owner;

    // CanRemove: caller must be Admin+ AND not removing self AND target role lower privilege (higher int) than caller
    // Owner (=1) can remove Admin (=2) since 2 > 1. Admin (=2) cannot remove Admin (=2) since 2 is not > 2.
    // Nobody can remove the Owner (Role == Owner means Role = 1, caller must have role < 1 — impossible).
    public bool CanRemove => !IsSelf
        && _callerRole <= ChatRole.Admin
        && Role > _callerRole;

    // CanMakeAdmin: only Owner can promote, and target must currently be Member
    public bool CanMakeAdmin => !IsSelf && _callerRole == ChatRole.Owner && Role == ChatRole.Member;

    // CanRevokeAdmin: only Owner can demote, and target must currently be Admin
    public bool CanRevokeAdmin => !IsSelf && _callerRole == ChatRole.Owner && Role == ChatRole.Admin;

    public IAsyncRelayCommand RemoveCommand { get; }
    public IAsyncRelayCommand MakeAdminCommand { get; }
    public IAsyncRelayCommand RevokeAdminCommand { get; }
    public IAsyncRelayCommand LeaveCommand { get; }

    public GroupMemberItemViewModel(
        ChatRole callerRole,
        Func<Guid, Task> removeAsync,
        Func<Guid, ChatRole, Task> updateRoleAsync,
        Func<Guid, Task> leaveAsync)
    {
        _callerRole = callerRole;

        RemoveCommand = new AsyncRelayCommand(
            () => removeAsync(UserId),
            () => CanRemove);

        MakeAdminCommand = new AsyncRelayCommand(
            () => updateRoleAsync(UserId, ChatRole.Admin),
            () => CanMakeAdmin);

        RevokeAdminCommand = new AsyncRelayCommand(
            () => updateRoleAsync(UserId, ChatRole.Member),
            () => CanRevokeAdmin);

        LeaveCommand = new AsyncRelayCommand(
            () => leaveAsync(UserId),
            () => CanLeave);
    }
}
