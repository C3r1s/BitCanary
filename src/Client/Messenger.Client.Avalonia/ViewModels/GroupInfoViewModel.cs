// Состояние и команды UI BitCanary для «GroupInfoViewModel».
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Messenger.Shared.Contracts;
using Messenger.Shared.Contracts.Dtos;

namespace Messenger.Client.Avalonia.ViewModels;

public sealed partial class GroupInfoViewModel : ViewModelBase
{
    private readonly Func<Guid, Guid, Task<ChatSummaryDto>> _addMemberAsync;
    private readonly Func<Guid, Guid, Task> _removeMemberAsync;
    private readonly Func<Guid, Guid, ChatRole, Task> _updateRoleAsync;
    private readonly Func<Guid, UpdateChatRequest, Task<ChatSummaryDto>> _updateChatAsync;
    private readonly Func<string, CancellationToken, Task<IReadOnlyCollection<UserProfileDto>>> _searchUsersAsync;
    private readonly Action _closeAction;

    private Guid _chatId;
    private Guid _currentUserId;
    private string _originalGroupName = string.Empty;
    private string _originalDescription = string.Empty;

    [ObservableProperty] private string _groupName = string.Empty;
    [ObservableProperty] private string _description = string.Empty;
    [ObservableProperty] private bool _isEditing;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _hasError;
    [ObservableProperty] private string _errorMessage = string.Empty;
    [ObservableProperty] private string _inviteUsername = string.Empty;

    public ObservableCollection<GroupMemberItemViewModel> Members { get; } = new();

    public bool IsAdmin { get; private set; }
    public bool IsOwner { get; private set; }
    public bool CanShowEditButton => IsAdmin && !IsEditing;
    public bool CanShowLeaveButton => !IsAdmin && !IsEditing;
    public int MemberCount => Members.Count;

    public IRelayCommand EditCommand { get; }
    public IAsyncRelayCommand SaveCommand { get; }
    public IRelayCommand CancelEditCommand { get; }
    public IAsyncRelayCommand LeaveGroupCommand { get; }
    public IAsyncRelayCommand AddMemberCommand { get; }
    public IRelayCommand CloseCommand { get; }

    public GroupInfoViewModel(
        Func<Guid, Guid, Task<ChatSummaryDto>> addMemberAsync,
        Func<Guid, Guid, Task> removeMemberAsync,
        Func<Guid, Guid, ChatRole, Task> updateRoleAsync,
        Func<Guid, UpdateChatRequest, Task<ChatSummaryDto>> updateChatAsync,
        Func<string, CancellationToken, Task<IReadOnlyCollection<UserProfileDto>>> searchUsersAsync,
        Action closeAction)
    {
        _addMemberAsync = addMemberAsync;
        _removeMemberAsync = removeMemberAsync;
        _updateRoleAsync = updateRoleAsync;
        _updateChatAsync = updateChatAsync;
        _searchUsersAsync = searchUsersAsync;
        _closeAction = closeAction;

        EditCommand = new RelayCommand(
            () => IsEditing = true,
            () => IsAdmin);

        SaveCommand = new AsyncRelayCommand(async () =>
        {
            IsBusy = true;
            HasError = false;
            ErrorMessage = string.Empty;
            try
            {
                var updated = await _updateChatAsync(_chatId,
                    new UpdateChatRequest(
                        GroupName.Trim().Length > 0 ? GroupName.Trim() : null,
                        Description));
                await LoadAsync(updated, _currentUserId);
                IsEditing = false;
            }
            catch (Exception ex)
            {
                HasError = true;
                ErrorMessage = ex.Message.ToLowerInvariant();
            }
            finally
            {
                IsBusy = false;
            }
        }, () => IsAdmin && !IsBusy);

        CancelEditCommand = new RelayCommand(() =>
        {
            GroupName = _originalGroupName;
            Description = _originalDescription;
            IsEditing = false;
            HasError = false;
            ErrorMessage = string.Empty;
        });

        LeaveGroupCommand = new AsyncRelayCommand(
            () => RemoveMemberAndReloadAsync(_currentUserId),
            () => !IsAdmin && !IsBusy);

        AddMemberCommand = new AsyncRelayCommand(AddMemberAsyncCore, () => IsAdmin && !IsBusy);

        CloseCommand = new RelayCommand(_closeAction);
    }

    private async Task AddMemberAsyncCore()
    {
        var query = InviteUsername.Trim();
        if (string.IsNullOrEmpty(query))
        {
            HasError = true;
            ErrorMessage = "enter a username";
            return;
        }

        IsBusy = true;
        HasError = false;
        ErrorMessage = string.Empty;
        try
        {
            var results = await _searchUsersAsync(query, CancellationToken.None).ConfigureAwait(true);
            var list = results.ToList();
            var match = list.FirstOrDefault(u =>
                string.Equals(u.UserName, query, StringComparison.OrdinalIgnoreCase));
            if (match is null && list.Count == 1)
                match = list[0];

            if (match is null)
            {
                HasError = true;
                ErrorMessage = "user not found or ambiguous; type the exact username";
                return;
            }

            if (match.Id == _currentUserId)
            {
                HasError = true;
                ErrorMessage = "cannot add yourself";
                return;
            }

            if (Members.Any(m => m.UserId == match.Id))
            {
                HasError = true;
                ErrorMessage = "user is already a member";
                return;
            }

            var updated = await _addMemberAsync(_chatId, match.Id).ConfigureAwait(true);
            InviteUsername = string.Empty;
            await LoadAsync(updated, _currentUserId).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            HasError = true;
            ErrorMessage = ex.Message.ToLower(CultureInfo.InvariantCulture);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task LoadAsync(ChatSummaryDto chat, Guid currentUserId)
    {
        _chatId = chat.Id;
        _currentUserId = currentUserId;

        GroupName = chat.Title;
        _originalGroupName = chat.Title;
        Description = chat.Description ?? string.Empty;
        _originalDescription = chat.Description ?? string.Empty;

        var currentMember = chat.Members.FirstOrDefault(x => x.UserId == currentUserId);
        var callerRole = currentMember?.Role ?? ChatRole.Member;

        IsAdmin = callerRole <= ChatRole.Admin;
        IsOwner = callerRole == ChatRole.Owner;
        OnPropertyChanged(nameof(IsAdmin));
        OnPropertyChanged(nameof(IsOwner));
        OnPropertyChanged(nameof(CanShowEditButton));
        OnPropertyChanged(nameof(CanShowLeaveButton));
        EditCommand.NotifyCanExecuteChanged();
        LeaveGroupCommand.NotifyCanExecuteChanged();
        AddMemberCommand.NotifyCanExecuteChanged();

        Members.Clear();
        foreach (var member in chat.Members.OrderBy(x => x.Role))
        {
            var item = new GroupMemberItemViewModel(
                callerRole,
                userId => RemoveMemberAndReloadAsync(userId),
                (userId, role) => UpdateRoleAndReloadAsync(userId, role),
                userId => RemoveMemberAndReloadAsync(userId))  // Leave = self-remove
            {
                UserId = member.UserId,
                DisplayName = member.DisplayName,
                Role = member.Role,
                IsSelf = member.UserId == currentUserId
            };
            Members.Add(item);
        }
        OnPropertyChanged(nameof(MemberCount));
    }

    partial void OnIsBusyChanged(bool value)
    {
        SaveCommand.NotifyCanExecuteChanged();
        LeaveGroupCommand.NotifyCanExecuteChanged();
        AddMemberCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsEditingChanged(bool value)
    {
        OnPropertyChanged(nameof(CanShowEditButton));
        OnPropertyChanged(nameof(CanShowLeaveButton));
    }

    private async Task RemoveMemberAndReloadAsync(Guid userId)
    {
        IsBusy = true;
        HasError = false;
        try
        {
            await _removeMemberAsync(_chatId, userId);
            var wasCurrentUser = userId == _currentUserId;
            if (wasCurrentUser)
            {
                _closeAction();
            }
            else
            {
                var item = Members.FirstOrDefault(m => m.UserId == userId);
                if (item is not null)
                {
                    Members.Remove(item);
                    OnPropertyChanged(nameof(MemberCount));
                }
            }
        }
        catch (Exception ex)
        {
            HasError = true;
            ErrorMessage = ex.Message.ToLowerInvariant();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task UpdateRoleAndReloadAsync(Guid userId, ChatRole newRole)
    {
        IsBusy = true;
        HasError = false;
        try
        {
            await _updateRoleAsync(_chatId, userId, newRole);
        }
        catch (Exception ex)
        {
            HasError = true;
            ErrorMessage = ex.Message.ToLowerInvariant();
        }
        finally
        {
            IsBusy = false;
        }
    }
}
