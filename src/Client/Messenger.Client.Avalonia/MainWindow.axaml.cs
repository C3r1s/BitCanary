using Avalonia.Controls;
using Avalonia.Input;
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

    /// <summary>
    /// Closes the safety number overlay when the scrim (outer Grid) is clicked directly —
    /// not when clicking inside the card.
    /// </summary>
    private void SafetyNumberScrim_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source == sender && DataContext is MainWindowViewModel vm)
        {
            vm.SafetyNumber.CloseCommand.Execute(null);
        }
    }
}
