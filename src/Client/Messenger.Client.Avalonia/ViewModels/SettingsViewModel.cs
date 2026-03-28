using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Messenger.Client.Avalonia.Models;
using Messenger.Shared.Contracts;

namespace Messenger.Client.Avalonia.ViewModels;

public sealed partial class SettingsViewModel : ViewModelBase
{
    private readonly Func<ThemePreference, Task> _changeThemeAsync;

    [ObservableProperty]
    private string _connectionStatus = "Offline";

    [ObservableProperty]
    private ThemeOption? _selectedThemeOption;

    [ObservableProperty]
    private bool _sendByEnter = true;

    [ObservableProperty]
    private bool _useCompactMode;

    [ObservableProperty]
    private bool _enableCustomEmoji = true;

    public ObservableCollection<ThemeOption> ThemeOptions { get; } =
    [
        new ThemeOption(ThemePreference.System, "System"),
        new ThemeOption(ThemePreference.Light, "Light"),
        new ThemeOption(ThemePreference.Dark, "Dark")
    ];

    public IAsyncRelayCommand SaveThemeCommand { get; }

    public SettingsViewModel(Func<ThemePreference, Task> changeThemeAsync)
    {
        _changeThemeAsync = changeThemeAsync;
        SelectedThemeOption = ThemeOptions[0];
        SaveThemeCommand = new AsyncRelayCommand(SaveThemeAsync);
    }

    private Task SaveThemeAsync() =>
        _changeThemeAsync(SelectedThemeOption?.Value ?? ThemePreference.System);
}
