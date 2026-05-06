// Состояние и команды UI BitCanary для «GroupMemberItemViewModel».
using CommunityToolkit.Mvvm.Input;
using Messenger.Shared.Contracts;

namespace Messenger.Client.Avalonia.ViewModels;

public sealed class GroupMemberItemViewModel : ViewModelBase
{
    public Guid UserId { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public ChatRole Role { get; init; }
    public bool IsSelf { get; init; }

    private readonly ChatRole _callerRole;

    public string RoleBadge => Role switch
    {
        ChatRole.Owner => "[ OWNER ]",
        ChatRole.Admin => "[ ADMIN ]",
        _ => "[ MEMBER ]"
    };

    public bool CanLeave => IsSelf && Role != ChatRole.Owner;

    public bool CanRemove => !IsSelf
        && _callerRole <= ChatRole.Admin
        && Role > _callerRole;

    public bool CanMakeAdmin => !IsSelf && _callerRole == ChatRole.Owner && Role == ChatRole.Member;

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
