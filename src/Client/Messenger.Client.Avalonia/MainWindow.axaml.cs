using Avalonia.Controls;
using Messenger.Client.Avalonia.ViewModels;

namespace Messenger.Client.Avalonia;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Opened += async (_, _) =>
        {
            if (DataContext is MainWindowViewModel viewModel)
            {
                await viewModel.InitializeCommand.ExecuteAsync(null);
            }
        };
    }
}
