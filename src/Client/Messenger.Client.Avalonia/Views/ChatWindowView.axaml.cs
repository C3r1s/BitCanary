using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Messenger.Client.Avalonia.ViewModels;

namespace Messenger.Client.Avalonia.Views;

public partial class ChatWindowView : UserControl
{
    private ScrollViewer? _scrollViewer;

    public ChatWindowView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is ChatWindowViewModel vm)
        {
            vm.Messages.CollectionChanged += OnMessagesChanged;
        }
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        _scrollViewer = this.FindControl<ScrollViewer>("MessagesScroll");
    }

    private void OnMessagesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add)
        {
            Dispatcher.UIThread.Post(() => _scrollViewer?.ScrollToEnd(), DispatcherPriority.Background);
        }
    }
}
