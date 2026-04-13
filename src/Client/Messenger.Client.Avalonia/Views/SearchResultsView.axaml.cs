using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using Messenger.Client.Avalonia.ViewModels;

namespace Messenger.Client.Avalonia.Views;

public partial class SearchResultsView : UserControl
{
    private TextBox? _searchQueryBox;
    private ListBox? _resultsList;

    public SearchResultsView()
    {
        InitializeComponent();
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        _searchQueryBox = this.FindControl<TextBox>("SearchQueryBox");
        _resultsList    = this.FindControl<ListBox>("ResultsList");

        if (_resultsList is not null)
            _resultsList.SelectionChanged += OnResultsListSelectionChanged;

        // If already visible on first load, focus immediately
        if (IsVisible)
            _searchQueryBox?.Focus();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsVisibleProperty && change.NewValue is true)
        {
            // Auto-focus search box when view becomes visible
            _searchQueryBox?.Focus();
        }
    }

    private void OnResultsListSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_resultsList?.SelectedItem is SearchResultItemViewModel result &&
            DataContext is SearchViewModel vm)
        {
            vm.SelectResultCommand.Execute(result);
            _resultsList.SelectedItem = null;
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape && DataContext is SearchViewModel vm)
        {
            // Clear query and close search mode via Reset
            vm.Reset();
            e.Handled = true;
        }
    }
}
