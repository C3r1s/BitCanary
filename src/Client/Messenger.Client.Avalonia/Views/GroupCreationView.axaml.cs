using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Messenger.Client.Avalonia.ViewModels;

namespace Messenger.Client.Avalonia.Views;

public partial class GroupCreationView : UserControl
{
    private TextBox? _groupNameInput;
    private ListBox? _memberSearchResults;

    public GroupCreationView()
    {
        InitializeComponent();
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        _groupNameInput = this.FindControl<TextBox>("GroupNameInput");
        _memberSearchResults = this.FindControl<ListBox>("MemberSearchResults");

        if (_memberSearchResults is not null)
        {
            _memberSearchResults.SelectionChanged += OnMemberSearchResultsSelectionChanged;
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsVisibleProperty && change.NewValue is true)
        {
            _groupNameInput?.Focus();
        }
    }

    private void OnMemberSearchResultsSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_memberSearchResults?.SelectedItem is UserResultItemViewModel item &&
            DataContext is GroupCreationViewModel vm)
        {
            vm.AddMemberCommand.Execute(item);
            var list = _memberSearchResults;
            Dispatcher.UIThread.Post(() => list.SelectedItem = null, DispatcherPriority.Background);
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape && DataContext is GroupCreationViewModel vm)
        {
            vm.Reset();
            var chatListView = this.FindAncestorOfType<ChatListView>();
            if (chatListView?.DataContext is ChatListViewModel chatListVm)
            {
                chatListVm.ToggleGroupCreationCommand.Execute(null);
            }
            e.Handled = true;
        }
    }
}
