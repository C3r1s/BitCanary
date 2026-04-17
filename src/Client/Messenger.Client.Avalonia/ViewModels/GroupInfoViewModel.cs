using System.Collections.ObjectModel;
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

    public ObservableCollection<GroupMemberItemViewModel> Members { get; } = new();

    public bool IsAdmin { get; private set; }
    public bool IsOwner { get; private set; }
    public int MemberCount => Members.Count;

    public IRelayCommand EditCommand { get; }
    public IAsyncRelayCommand SaveCommand { get; }
    public IRelayCommand CancelEditCommand { get; }
    public IRelayCommand CloseCommand { get; }

    public GroupInfoViewModel(
        Func<Guid, Guid, Task<ChatSummaryDto>> addMemberAsync,
        Func<Guid, Guid, Task> removeMemberAsync,
        Func<Guid, Guid, ChatRole, Task> updateRoleAsync,
        Func<Guid, UpdateChatRequest, Task<ChatSummaryDto>> updateChatAsync,
        Action closeAction)
    {
        _addMemberAsync = addMemberAsync;
        _removeMemberAsync = removeMemberAsync;
        _updateRoleAsync = updateRoleAsync;
        _updateChatAsync = updateChatAsync;
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
                // Reload from updated summary
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

        CloseCommand = new RelayCommand(_closeAction);
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

        // IsAdmin means the user can perform admin actions (Owner is also Admin+)
        IsAdmin = callerRole <= ChatRole.Admin;
        IsOwner = callerRole == ChatRole.Owner;
        OnPropertyChanged(nameof(IsAdmin));
        OnPropertyChanged(nameof(IsOwner));

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

    private async Task RemoveMemberAndReloadAsync(Guid userId)
    {
        IsBusy = true;
        HasError = false;
        try
        {
            await _removeMemberAsync(_chatId, userId);
            // After leaving, close the panel (caller was removed or left)
            var wasCurrentUser = userId == _currentUserId;
            // Signal that a reload is needed by closing the panel on leave
            if (wasCurrentUser)
            {
                _closeAction();
            }
            else
            {
                // FIX-05: remove the member from the observable collection so the UI reflects the removal immediately.
                var item = Members.FirstOrDefault(m => m.UserId == userId);
                if (item is not null)
                {
                    Members.Remove(item);
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
