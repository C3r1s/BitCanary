using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Messenger.Client.Avalonia.ViewModels;

namespace Messenger.Client.Avalonia.Views;

public partial class UserSearchView : UserControl
{
    private TextBox? _searchInput;
    private ListBox? _resultsList;

    public UserSearchView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        // bindings handle the rest
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        _searchInput = this.FindControl<TextBox>("SearchInput");
        _resultsList = this.FindControl<ListBox>("ResultsList");

        if (_resultsList is not null)
        {
            _resultsList.SelectionChanged += OnResultsListSelectionChanged;
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsVisibleProperty && change.NewValue is true)
        {
            // Auto-focus search input when view becomes visible
            _searchInput?.Focus();
        }
    }

    private void OnResultsListSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_resultsList?.SelectedItem is UserResultItemViewModel item &&
            DataContext is UserSearchViewModel vm)
        {
            vm.SelectUserCommand.Execute(item);
            // Defer the reset to avoid re-entrant SelectionChanged while the
            // collection may be in the middle of an update (ArgumentOutOfRangeException).
            var list = _resultsList;
            Dispatcher.UIThread.Post(() => list.SelectedItem = null, DispatcherPriority.Background);
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape && DataContext is UserSearchViewModel vm)
        {
            // Reset search state and close user search mode via ToggleUserSearchCommand
            vm.Reset();

            // Walk up to find the ChatListViewModel and toggle user search off
            var chatListView = this.FindAncestorOfType<ChatListView>();
            if (chatListView?.DataContext is ChatListViewModel chatListVm)
            {
                chatListVm.ToggleUserSearchCommand.Execute(null);
            }

            e.Handled = true;
        }
    }
}
