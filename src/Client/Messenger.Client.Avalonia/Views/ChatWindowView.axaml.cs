// Код-behind «ChatWindowView.axaml»: обработка UI и связь с ViewModel.
using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Messenger.Client.Avalonia.ViewModels;

namespace Messenger.Client.Avalonia.Views;

public partial class ChatWindowView : UserControl
{
    private ScrollViewer? _scrollViewer;
    private TextBox? _findQueryBox;
    private ChatWindowViewModel? _boundViewModel;
    private bool _stickToBottom = true;

    public ChatWindowView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        _scrollViewer = this.FindControl<ScrollViewer>("MessagesScroll");
        _findQueryBox = this.FindControl<TextBox>("FindQueryBox");
        if (_scrollViewer is not null)
        {
            _scrollViewer.ScrollChanged += OnScrollChanged;
        }
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_boundViewModel is not null)
        {
            _boundViewModel.Messages.CollectionChanged -= OnMessagesChanged;
            _boundViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            foreach (var message in _boundViewModel.Messages)
            {
                message.PropertyChanged -= OnMessagePropertyChanged;
            }
        }

        if (DataContext is ChatWindowViewModel vm)
        {
            _boundViewModel = vm;
            vm.Messages.CollectionChanged += OnMessagesChanged;
            vm.PropertyChanged += OnViewModelPropertyChanged;
            foreach (var message in vm.Messages)
            {
                message.PropertyChanged += OnMessagePropertyChanged;
            }
        }
        else
        {
            _boundViewModel = null;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ChatWindowViewModel.IsFindBarVisible) &&
            DataContext is ChatWindowViewModel vm && vm.IsFindBarVisible)
        {
            Dispatcher.UIThread.Post(() => _findQueryBox?.Focus(), DispatcherPriority.Background);
        }
    }

    private void OnMessagesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (var item in e.OldItems.OfType<MessageItemViewModel>())
            {
                item.PropertyChanged -= OnMessagePropertyChanged;
            }
        }

        if (e.NewItems is not null)
        {
            foreach (var item in e.NewItems.OfType<MessageItemViewModel>())
            {
                item.PropertyChanged += OnMessagePropertyChanged;
            }
        }

        if (_stickToBottom &&
            (e.Action == NotifyCollectionChangedAction.Add || e.Action == NotifyCollectionChangedAction.Reset))
        {
            Dispatcher.UIThread.Post(() => _scrollViewer?.ScrollToEnd(), DispatcherPriority.Background);
        }
    }

    private void OnMessagePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_stickToBottom && e.PropertyName == nameof(MessageItemViewModel.InlineImage))
        {
            Dispatcher.UIThread.Post(() => _scrollViewer?.ScrollToEnd(), DispatcherPriority.Background);
        }
    }

    private void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        _stickToBottom = IsNearBottom();
    }

    private bool IsNearBottom()
    {
        if (_scrollViewer is null) return true;

        var remaining = _scrollViewer.Extent.Height - _scrollViewer.Viewport.Height - _scrollViewer.Offset.Y;
        return remaining <= 24;
    }

}
