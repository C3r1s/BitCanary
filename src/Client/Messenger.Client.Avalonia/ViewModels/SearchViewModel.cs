// Состояние и команды UI BitCanary для «SearchViewModel».
using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Messenger.Client.Avalonia.Services;
using Messenger.Shared.Contracts.Dtos;

namespace Messenger.Client.Avalonia.ViewModels;

public sealed partial class SearchViewModel : ViewModelBase
{
    private readonly ILocalSearchService _searchService;
    private readonly Action<SearchResultItemViewModel> _onResultSelected;
    private readonly Func<string, CancellationToken, Task<IReadOnlyCollection<UserProfileDto>>>? _userSearchAsync;
    private readonly Action<UserProfileDto>? _onUserSelected;
    private readonly Guid? _chatIdFilter;
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
    private string _emptyStateHeading = string.Empty;

    [ObservableProperty]
    private string _emptyStateBody = string.Empty;

    [ObservableProperty]
    private bool _isUserLookupMode;

    public bool ShowMessageResults => HasSearched && !IsUserLookupMode;
    public bool ShowUserResults => HasSearched && IsUserLookupMode;

    public ObservableCollection<SearchResultItemViewModel> SearchResults { get; } = new();
    public ObservableCollection<UserResultItemViewModel> UserResults { get; } = new();

    /// <summary>Raised on the UI thread immediately before SearchResults / UserResults are cleared so ListBox selection can be reset safely.</summary>
    public event EventHandler? BeforeSearchCollectionsMutation;

    public IRelayCommand<SearchResultItemViewModel> SelectResultCommand { get; }
    public IRelayCommand<UserResultItemViewModel> SelectUserCommand { get; }

    partial void OnHasSearchedChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowMessageResults));
        OnPropertyChanged(nameof(ShowUserResults));
    }

    partial void OnIsUserLookupModeChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowMessageResults));
        OnPropertyChanged(nameof(ShowUserResults));
    }

    public SearchViewModel(
        ILocalSearchService searchService,
        Action<SearchResultItemViewModel> onResultSelected,
        Func<string, CancellationToken, Task<IReadOnlyCollection<UserProfileDto>>>? userSearchAsync = null,
        Action<UserProfileDto>? onUserSelected = null,
        Guid? chatIdFilter = null)
    {
        _searchService = searchService;
        _onResultSelected = onResultSelected;
        _userSearchAsync = userSearchAsync;
        _onUserSelected = onUserSelected;
        _chatIdFilter = chatIdFilter;
        SelectResultCommand = new RelayCommand<SearchResultItemViewModel>(result =>
        {
            if (result is not null)
                _onResultSelected(result);
        });
        SelectUserCommand = new RelayCommand<UserResultItemViewModel>(user =>
        {
            if (user is not null)
                _onUserSelected?.Invoke(user.Dto);
        });
    }

    private void NotifyBeforeSearchCollectionsMutation()
    {
        BeforeSearchCollectionsMutation?.Invoke(this, EventArgs.Empty);
    }

    partial void OnSearchQueryChanged(string value)
    {
        _debounceCts?.Cancel();
        _debounceCts?.Dispose();
        _debounceCts = null;

        if (string.IsNullOrWhiteSpace(value))
        {
            NotifyBeforeSearchCollectionsMutation();
            SearchResults.Clear();
            UserResults.Clear();
            HasSearched = false;
            ShowNoResults = false;
            IsUserLookupMode = false;
            EmptyStateHeading = string.Empty;
            EmptyStateBody = string.Empty;
            return;
        }

        var trimmed = value.Trim();
        var isUserLookup = trimmed.StartsWith('@');
        IsUserLookupMode = isUserLookup;
        NotifyBeforeSearchCollectionsMutation();
        SearchResults.Clear();
        UserResults.Clear();

        if (isUserLookup)
        {
            var userQuery = trimmed[1..].Trim();
            if (userQuery.Length < 2 || _userSearchAsync is null)
            {
                HasSearched = false;
                ShowNoResults = false;
                EmptyStateHeading = string.Empty;
                EmptyStateBody = string.Empty;
                return;
            }
        }

        var cts = new CancellationTokenSource();
        _debounceCts = cts;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(300, cts.Token);
                await Dispatcher.UIThread.InvokeAsync(() => IsLoading = true);

                var query = value.Trim();
                var userMode = query.StartsWith('@');
                IReadOnlyCollection<SearchResult> messageResults = Array.Empty<SearchResult>();
                IReadOnlyCollection<UserProfileDto> userResults = Array.Empty<UserProfileDto>();

                if (userMode && _userSearchAsync is not null)
                {
                    userResults = await _userSearchAsync(query[1..].Trim(), cts.Token);
                }
                else
                {
                    messageResults = await _searchService.SearchAsync(query, _chatIdFilter, cts.Token);
                }

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    IsLoading = false;
                    NotifyBeforeSearchCollectionsMutation();
                    SearchResults.Clear();
                    UserResults.Clear();
                    IsUserLookupMode = userMode;
                    foreach (var r in messageResults)
                        SearchResults.Add(new SearchResultItemViewModel(r));
                    foreach (var u in userResults)
                        UserResults.Add(new UserResultItemViewModel(u));

                    HasSearched = true;
                    var resultCount = userMode ? userResults.Count : messageResults.Count;

                    if (resultCount == 0)
                    {
                        EmptyStateHeading = userMode
                            ? $"No users matched '{query}'"
                            : $"No messages matched '{query}'";
                        EmptyStateBody = userMode
                            ? "Use @ followed by a username or display name."
                            : "Try a different word or check your spelling.";
                        ShowNoResults = true;
                    }
                    else
                    {
                        EmptyStateHeading = string.Empty;
                        EmptyStateBody = string.Empty;
                        ShowNoResults = false;
                    }
                });
            }
            catch (OperationCanceledException)
            {
            }
            catch
            {
                await Dispatcher.UIThread.InvokeAsync(() => IsLoading = false);
            }
        }, cts.Token);
    }

    public void Reset()
    {
        _debounceCts?.Cancel();
        _debounceCts?.Dispose();
        _debounceCts = null;

        var queryWasEmpty = string.IsNullOrWhiteSpace(SearchQuery);
        SearchQuery = string.Empty;
        if (queryWasEmpty)
        {
            NotifyBeforeSearchCollectionsMutation();
            SearchResults.Clear();
            UserResults.Clear();
            HasSearched = false;
            ShowNoResults = false;
            IsUserLookupMode = false;
            EmptyStateHeading = string.Empty;
            EmptyStateBody = string.Empty;
        }

        IsLoading = false;
    }
}
