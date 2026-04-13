using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Messenger.Client.Avalonia.ViewModels;

namespace Messenger.Client.Avalonia.Views;

public partial class MessageInputView : UserControl
{
    public MessageInputView()
    {
        InitializeComponent();
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
            vm.AttachFile(files[0].Name);
    }
}
