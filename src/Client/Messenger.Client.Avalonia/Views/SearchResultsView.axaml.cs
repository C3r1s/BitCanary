// Код-behind «SearchResultsView.axaml»: обработка UI и связь с ViewModel.
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
    private ListBox? _userResultsList;

    public SearchResultsView()
    {
        InitializeComponent();
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        _searchQueryBox = this.FindControl<TextBox>("SearchQueryBox");
        _resultsList    = this.FindControl<ListBox>("ResultsList");
        _userResultsList = this.FindControl<ListBox>("UserResultsList");

        if (_resultsList is not null)
            _resultsList.SelectionChanged += OnResultsListSelectionChanged;
        if (_userResultsList is not null)
            _userResultsList.SelectionChanged += OnUserResultsListSelectionChanged;

        if (IsVisible)
            _searchQueryBox?.Focus();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsVisibleProperty && change.NewValue is true)
        {
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

    private void OnUserResultsListSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_userResultsList?.SelectedItem is UserResultItemViewModel user &&
            DataContext is SearchViewModel vm)
        {
            vm.SelectUserCommand.Execute(user);
            _userResultsList.SelectedItem = null;
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape && DataContext is SearchViewModel vm)
        {
            vm.Reset();
            e.Handled = true;
        }
    }
}
