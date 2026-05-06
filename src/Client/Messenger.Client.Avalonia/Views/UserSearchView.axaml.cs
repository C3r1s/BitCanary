// Код-behind «UserSearchView.axaml»: обработка UI и связь с ViewModel.
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
            _searchInput?.Focus();
        }
    }

    private void OnResultsListSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_resultsList?.SelectedItem is UserResultItemViewModel item &&
            DataContext is UserSearchViewModel vm)
        {
            vm.SelectUserCommand.Execute(item);
            var list = _resultsList;
            Dispatcher.UIThread.Post(() => list.SelectedItem = null, DispatcherPriority.Background);
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape && DataContext is UserSearchViewModel vm)
        {
            vm.Reset();

            var chatListView = this.FindAncestorOfType<ChatListView>();
            if (chatListView?.DataContext is ChatListViewModel chatListVm)
            {
                chatListVm.ToggleUserSearchCommand.Execute(null);
            }

            e.Handled = true;
        }
    }
}
