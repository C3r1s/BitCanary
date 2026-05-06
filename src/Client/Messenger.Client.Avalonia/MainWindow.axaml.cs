// Обработчики главного окна: горячие клавиши, модальные оверлеи, фокус поиска.
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
        else if (e.Key == Key.Escape && vm.IsConfirmingDeleteChat)
        {
            vm.CancelDeleteChatCommand.Execute(null);
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

    private void SafetyNumberScrim_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source == sender && DataContext is MainWindowViewModel vm)
        {
            vm.SafetyNumber.CloseCommand.Execute(null);
        }
    }

    private void SettingsScrim_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source == sender && DataContext is MainWindowViewModel vm)
        {
            vm.ToggleSettingsCommand.Execute(null);
        }
    }

    private void DeleteChatScrim_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source == sender && DataContext is MainWindowViewModel vm)
        {
            vm.CancelDeleteChatCommand.Execute(null);
        }
    }
}
