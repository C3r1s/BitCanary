using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Threading;
using Messenger.Client.Avalonia.ViewModels;

namespace Messenger.Client.Avalonia.Views;

public partial class ChatWindowView : UserControl
{
    private ScrollViewer? _scrollViewer;
    private TextBox? _findQueryBox;

    public ChatWindowView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        this.KeyDown += OnKeyDown;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is ChatWindowViewModel vm)
        {
            vm.Messages.CollectionChanged += OnMessagesChanged;
            vm.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ChatWindowViewModel.IsFindBarVisible) &&
            DataContext is ChatWindowViewModel vm && vm.IsFindBarVisible)
        {
            // Auto-focus the find query box when the find-bar opens
            Dispatcher.UIThread.Post(() => _findQueryBox?.Focus(), DispatcherPriority.Background);
        }
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        _scrollViewer = this.FindControl<ScrollViewer>("MessagesScroll");
        _findQueryBox = this.FindControl<TextBox>("FindQueryBox");
    }

    private void OnMessagesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add)
        {
            Dispatcher.UIThread.Post(() => _scrollViewer?.ScrollToEnd(), DispatcherPriority.Background);
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.F && e.KeyModifiers == KeyModifiers.Control)
        {
            if (DataContext is ChatWindowViewModel vm)
            {
                vm.OpenFindBarCommand.Execute(null);
                e.Handled = true;
            }
        }
        else if (e.Key == Key.Escape &&
                 DataContext is ChatWindowViewModel vm2 &&
                 vm2.IsFindBarVisible)
        {
            vm2.CloseFindBarCommand.Execute(null);
            e.Handled = true;
        }
    }
}
