// Состояние и команды UI BitCanary для «UserSearchViewModel».
using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Messenger.Client.Avalonia.Services;
using Messenger.Shared.Contracts.Dtos;

namespace Messenger.Client.Avalonia.ViewModels;

public sealed partial class UserSearchViewModel : ViewModelBase
{
    private readonly IMessengerApiClient _apiClient;
    private readonly Func<UserProfileDto, Task> _onUserSelected;
    private readonly Func<Action, Task> _uiDispatch;
    private CancellationTokenSource? _debounceCts;

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _hasSearched;

    [ObservableProperty]
    private bool _showNoResults;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _hasError;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    public ObservableCollection<UserResultItemViewModel> Results { get; } = new();

    public IRelayCommand<UserResultItemViewModel> SelectUserCommand { get; }

    public UserSearchViewModel(IMessengerApiClient apiClient, Func<UserProfileDto, Task> onUserSelected,
        Func<Action, Task>? uiDispatch = null)
    {
        _apiClient = apiClient;
        _onUserSelected = onUserSelected;
        _uiDispatch = uiDispatch ?? (action => Dispatcher.UIThread.InvokeAsync(action).GetTask());

        SelectUserCommand = new AsyncRelayCommand<UserResultItemViewModel>(
            async item => { if (item is not null) await _onUserSelected(item.Dto); },
            item => !IsBusy);
    }

    partial void OnSearchQueryChanged(string value)
    {
        _debounceCts?.Cancel();
        _debounceCts?.Dispose();
        _debounceCts = null;

        HasError = false;
        ErrorMessage = string.Empty;

        if (string.IsNullOrWhiteSpace(value) || value.Trim().Length < 2)
        {
            Results.Clear();
            HasSearched = false;
            ShowNoResults = false;
            return;
        }

        var cts = new CancellationTokenSource();
        _debounceCts = cts;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(300, cts.Token);

                await _uiDispatch(() => IsLoading = true);

                var results = await _apiClient.SearchUsersAsync(value.Trim(), cts.Token);

                await _uiDispatch(() =>
                {
                    IsLoading = false;
                    Results.Clear();
                    foreach (var r in results)
                        Results.Add(new UserResultItemViewModel(r));

                    HasSearched = true;
                    ShowNoResults = results.Count == 0;
                });
            }
            catch (OperationCanceledException)
            {
            }
            catch
            {
                await _uiDispatch(() =>
                {
                    IsLoading = false;
                    HasError = true;
                    ErrorMessage = "could not reach server -- check your connection";
                });
            }
        }, cts.Token);
    }

    partial void OnIsBusyChanged(bool value)
    {
        SelectUserCommand.NotifyCanExecuteChanged();
    }

    public void Reset()
    {
        _debounceCts?.Cancel();
        _debounceCts?.Dispose();
        _debounceCts = null;

        Dispatcher.UIThread.Post(() =>
        {
            Results.Clear();
            SearchQuery = string.Empty;
            HasSearched = false;
            ShowNoResults = false;
            IsLoading = false;
            IsBusy = false;
            HasError = false;
            ErrorMessage = string.Empty;
        }, DispatcherPriority.Background);
    }
}
