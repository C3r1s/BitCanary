// Состояние и команды UI BitCanary для «GroupCreationViewModel».
using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Messenger.Shared.Contracts;
using Messenger.Shared.Contracts.Dtos;

namespace Messenger.Client.Avalonia.ViewModels;

public sealed partial class GroupCreationViewModel : ViewModelBase
{
    private readonly Func<string, CancellationToken, Task<IReadOnlyCollection<UserProfileDto>>> _searchAsync;
    private readonly Func<CreateChatRequest, Task> _createGroupAsync;
    private CancellationTokenSource? _debounceCts;

    [ObservableProperty] private string _groupName = string.Empty;
    [ObservableProperty] private string _description = string.Empty;
    [ObservableProperty] private string _memberSearchQuery = string.Empty;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _hasError;
    [ObservableProperty] private string _errorMessage = string.Empty;

    public ObservableCollection<UserResultItemViewModel> SearchResults { get; } = new();
    public ObservableCollection<UserResultItemViewModel> SelectedMembers { get; } = new();

    public bool CanCreate => GroupName.Trim().Length > 0 && SelectedMembers.Count > 0 && !IsBusy;

    public IAsyncRelayCommand CreateGroupCommand { get; }
    public IRelayCommand<UserResultItemViewModel> AddMemberCommand { get; }
    public IRelayCommand<UserResultItemViewModel> RemoveMemberCommand { get; }

    public GroupCreationViewModel(
        Func<string, CancellationToken, Task<IReadOnlyCollection<UserProfileDto>>> searchAsync,
        Func<CreateChatRequest, Task> createGroupAsync)
    {
        _searchAsync = searchAsync;
        _createGroupAsync = createGroupAsync;

        AddMemberCommand = new RelayCommand<UserResultItemViewModel>(item =>
        {
            if (item is null) return;
            if (SelectedMembers.Any(x => x.UserId == item.UserId)) return;
            SelectedMembers.Add(item);
            OnPropertyChanged(nameof(CanCreate));
            CreateGroupCommand.NotifyCanExecuteChanged();
            Dispatcher.UIThread.Post(() =>
            {
                SearchResults.Clear();
                MemberSearchQuery = string.Empty;
            }, DispatcherPriority.Background);
        });

        RemoveMemberCommand = new RelayCommand<UserResultItemViewModel>(item =>
        {
            if (item is null) return;
            SelectedMembers.Remove(item);
            OnPropertyChanged(nameof(CanCreate));
            CreateGroupCommand.NotifyCanExecuteChanged();
        });

        CreateGroupCommand = new AsyncRelayCommand(async () =>
        {
            IsBusy = true;
            HasError = false;
            ErrorMessage = string.Empty;
            try
            {
                var request = new CreateChatRequest(
                    GroupName.Trim(),
                    ChatType.Group,
                    string.IsNullOrWhiteSpace(Description) ? null : Description.Trim(),
                    SelectedMembers.Select(x => x.UserId).ToArray());
                await _createGroupAsync(request);
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
        }, () => CanCreate);
    }

    partial void OnGroupNameChanged(string _)
    {
        OnPropertyChanged(nameof(CanCreate));
        CreateGroupCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsBusyChanged(bool _)
    {
        OnPropertyChanged(nameof(CanCreate));
        CreateGroupCommand.NotifyCanExecuteChanged();
    }

    partial void OnMemberSearchQueryChanged(string value)
    {
        _debounceCts?.Cancel();
        _debounceCts?.Dispose();
        _debounceCts = null;

        if (string.IsNullOrWhiteSpace(value) || value.Trim().Length < 2)
        {
            SearchResults.Clear();
            return;
        }

        var cts = new CancellationTokenSource();
        _debounceCts = cts;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(300, cts.Token);
                var results = await _searchAsync(value.Trim(), cts.Token);
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    SearchResults.Clear();
                    foreach (var r in results)
                        SearchResults.Add(new UserResultItemViewModel(r));
                });
            }
            catch (OperationCanceledException) { }
            catch
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    HasError = true;
                    ErrorMessage = "could not reach server -- check your connection";
                });
            }
        }, cts.Token);
    }

    public void Reset()
    {
        _debounceCts?.Cancel();
        _debounceCts?.Dispose();
        _debounceCts = null;

        Dispatcher.UIThread.Post(() =>
        {
            GroupName = string.Empty;
            Description = string.Empty;
            MemberSearchQuery = string.Empty;
            SearchResults.Clear();
            SelectedMembers.Clear();
            IsBusy = false;
            HasError = false;
            ErrorMessage = string.Empty;
            OnPropertyChanged(nameof(CanCreate));
            CreateGroupCommand.NotifyCanExecuteChanged();
        }, DispatcherPriority.Background);
    }
}
