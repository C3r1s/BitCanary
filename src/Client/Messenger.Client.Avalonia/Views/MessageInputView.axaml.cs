// Код-behind «MessageInputView.axaml»: обработка UI и связь с ViewModel.
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Messenger.Client.Avalonia.ViewModels;

namespace Messenger.Client.Avalonia.Views;

public partial class MessageInputView : UserControl
{
    private TextBox? _inputBox;

    public MessageInputView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (_inputBox is not null) return;
        _inputBox = this.FindControl<TextBox>("MessageInputBox");
        if (_inputBox is null) return;

        _inputBox.AddHandler(InputElement.KeyDownEvent, OnInputBoxKeyDown,
            RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    private async void OnAttachClicked(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Attach file",
            AllowMultiple = false
        });

        if (files.Count > 0 && DataContext is MessageInputViewModel vm)
            vm.AttachFile(files[0]);
    }

    private void OnInputBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter && e.Key != Key.Return) return;
        var shiftHeld = (e.KeyModifiers & KeyModifiers.Shift) == KeyModifiers.Shift;
        if (shiftHeld) return;

        if (DataContext is not MessageInputViewModel vm) return;
        if (vm.SendCommand.CanExecute(null))
        {
            vm.SendCommand.Execute(null);
        }
        e.Handled = true;
    }
}
