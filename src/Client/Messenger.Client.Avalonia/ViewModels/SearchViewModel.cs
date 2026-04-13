using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Messenger.Client.Avalonia.Services;

namespace Messenger.Client.Avalonia.ViewModels;

public sealed partial class SearchViewModel : ViewModelBase
{
    private readonly ILocalSearchService _searchService;
    private readonly Action<SearchResultItemViewModel> _onResultSelected;
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

    public ObservableCollection<SearchResultItemViewModel> SearchResults { get; } = new();

    public IRelayCommand<SearchResultItemViewModel> SelectResultCommand { get; }

    public SearchViewModel(
        ILocalSearchService searchService,
        Action<SearchResultItemViewModel> onResultSelected,
        Guid? chatIdFilter = null)
    {
        _searchService = searchService;
        _onResultSelected = onResultSelected;
        _chatIdFilter = chatIdFilter;
        SelectResultCommand = new RelayCommand<SearchResultItemViewModel>(result =>
        {
            if (result is not null)
                _onResultSelected(result);
        });
    }

    partial void OnSearchQueryChanged(string value)
    {
        _debounceCts?.Cancel();
        _debounceCts?.Dispose();
        _debounceCts = null;

        if (string.IsNullOrWhiteSpace(value))
        {
            SearchResults.Clear();
            HasSearched = false;
            ShowNoResults = false;
            EmptyStateHeading = string.Empty;
            EmptyStateBody = string.Empty;
            return;
        }

        var cts = new CancellationTokenSource();
        _debounceCts = cts;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(300, cts.Token);
                await Dispatcher.UIThread.InvokeAsync(() => IsLoading = true);

                var results = await _searchService.SearchAsync(value, _chatIdFilter, cts.Token);

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    IsLoading = false;
                    SearchResults.Clear();
                    foreach (var r in results)
                        SearchResults.Add(new SearchResultItemViewModel(r));

                    HasSearched = true;

                    if (results.Count == 0)
                    {
                        EmptyStateHeading = $"No messages matched '{value}'";
                        EmptyStateBody = "Try a different word or check your spelling.";
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
                // Debounced or cancelled — expected
            }
            catch
            {
                await Dispatcher.UIThread.InvokeAsync(() => IsLoading = false);
            }
        }, cts.Token);
    }

    /// <summary>Clears query, results, and HasSearched. Called when search mode is closed.</summary>
    public void Reset()
    {
        _debounceCts?.Cancel();
        _debounceCts?.Dispose();
        _debounceCts = null;

        SearchQuery = string.Empty;
        SearchResults.Clear();
        HasSearched = false;
        ShowNoResults = false;
        IsLoading = false;
        EmptyStateHeading = string.Empty;
        EmptyStateBody = string.Empty;
    }
}
