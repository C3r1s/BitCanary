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

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (DataContext is not MainWindowViewModel vm) return;

        if (e.Key == Key.F && e.KeyModifiers == KeyModifiers.Control)
        {
            vm.ChatWindow.OpenFindBarCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && vm.ChatWindow.IsFindBarVisible)
        {
            vm.ChatWindow.CloseFindBarCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && vm.ChatList.SelectedChat is not null
                 && !vm.IsShowingSafetyNumber && !vm.IsShowingSettings
                 && !vm.ChatList.IsUserSearchMode && !vm.ChatList.IsSearchMode)
        {
            vm.CloseActiveChatCommand.Execute(null);
            e.Handled = true;
        }
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

    /// <summary>
    /// Closes the settings modal overlay when the scrim is clicked directly.
    /// </summary>
    private void SettingsScrim_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source == sender && DataContext is MainWindowViewModel vm)
        {
            vm.ToggleSettingsCommand.Execute(null);
        }
    }
}
