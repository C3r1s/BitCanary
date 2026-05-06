// Состояние и команды UI BitCanary для «SettingsViewModel».
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Messenger.Client.Avalonia.Models;
using Messenger.Client.Avalonia.Services.Crypto;
using Messenger.Shared.Contracts;

namespace Messenger.Client.Avalonia.ViewModels;

public sealed partial class SettingsViewModel : ViewModelBase
{
    private readonly Func<ThemePreference, Task> _changeThemeAsync;
    private readonly KeyPublicationService? _keyPublicationService;

    [ObservableProperty]
    private string _connectionStatus = "Offline";

    [ObservableProperty]
    private ThemeOption? _selectedThemeOption;

    [ObservableProperty]
    private bool _sendByEnter = true;

    [ObservableProperty]
    private bool _enableCustomEmoji = true;


    [ObservableProperty]
    private bool _showNotifications = true;

    [ObservableProperty]
    private bool _showSenderName = true;


    [ObservableProperty]
    private string _spkRotationDate = "Last rotated: Unknown";

    [ObservableProperty]
    private bool _canShowSafetyNumber;

    [ObservableProperty]
    private bool _isRegenerating;

    [ObservableProperty]
    private bool _isConfirmingRegenerate;

    public IRelayCommand? ShowSafetyNumberCommand { get; set; }

    public IRelayCommand? LogoutCommand { get; set; }

    public IAsyncRelayCommand RegenerateIdentityKeyCommand { get; }

    public IRelayCommand ShowRegenerateConfirmCommand { get; }

    public IRelayCommand CancelRegenerateCommand { get; }


    public ObservableCollection<ThemeOption> ThemeOptions { get; } =
    [
        new ThemeOption(ThemePreference.Terminal, "Terminal (Hacker)")
    ];


    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTerminalThemeSelected))]
    private ThemeOption? _selectedThemeOptionBacking;

    public bool IsTerminalThemeSelected =>
        SelectedThemeOption?.Value == ThemePreference.Terminal;

    public ObservableCollection<TerminalSchemeOption> TerminalSchemeOptions { get; } =
    [
        new TerminalSchemeOption(TerminalColorScheme.MatrixGreen, "Green-on-black (Matrix)"),
        new TerminalSchemeOption(TerminalColorScheme.AmberTerminal, "Amber Terminal"),
        new TerminalSchemeOption(TerminalColorScheme.CyberpunkCyan, "Cyberpunk (Cyan)")
    ];

    [ObservableProperty]
    private TerminalSchemeOption? _selectedTerminalScheme;

    private readonly Action<Messenger.Shared.Contracts.TerminalColorScheme>? _applyTerminalScheme;
    private bool _suppressTerminalSchemePersistence;


    public SettingsViewModel(
        Func<ThemePreference, Task> changeThemeAsync,
        KeyPublicationService? keyPublicationService = null,
        Action<Messenger.Shared.Contracts.TerminalColorScheme>? applyTerminalScheme = null)
    {
        _changeThemeAsync = changeThemeAsync;
        _keyPublicationService = keyPublicationService;
        _applyTerminalScheme = applyTerminalScheme;
        _selectedThemeOption = ThemeOptions[0];       // bypass setter — avoids firing _changeThemeAsync before MainWindowViewModel.Settings is assigned
        _selectedTerminalScheme = TerminalSchemeOptions[0];

        RegenerateIdentityKeyCommand = new AsyncRelayCommand(RegenerateIdentityKeyAsync);
        ShowRegenerateConfirmCommand = new RelayCommand(() => IsConfirmingRegenerate = true);
        CancelRegenerateCommand = new RelayCommand(() => IsConfirmingRegenerate = false);
    }

    partial void OnSelectedThemeOptionChanged(ThemeOption? value)
    {
        OnPropertyChanged(nameof(IsTerminalThemeSelected));
        if (value is not null)
            _ = _changeThemeAsync(value.Value);
    }

    partial void OnSelectedTerminalSchemeChanged(TerminalSchemeOption? value)
    {
        if (value is null) return;

        _applyTerminalScheme?.Invoke(value.Value);
        if (_suppressTerminalSchemePersistence) return;

        if (SelectedThemeOption?.Value != ThemePreference.Terminal)
        {
            _selectedThemeOption = ThemeOptions.FirstOrDefault(x => x.Value == ThemePreference.Terminal)
                                   ?? ThemeOptions[0];
            OnPropertyChanged(nameof(SelectedThemeOption));
            OnPropertyChanged(nameof(IsTerminalThemeSelected));
        }
        _ = _changeThemeAsync(ThemePreference.Terminal);
    }

    public void SelectTerminalSchemeFromSettings(TerminalColorScheme scheme)
    {
        var option = TerminalSchemeOptions.FirstOrDefault(x => x.Value == scheme)
                     ?? TerminalSchemeOptions[0];
        _suppressTerminalSchemePersistence = true;
        try
        {
            SelectedTerminalScheme = option;
        }
        finally
        {
            _suppressTerminalSchemePersistence = false;
        }

        _applyTerminalScheme?.Invoke(option.Value);
    }

    public void RefreshSpkRotationDate()
    {
        if (_keyPublicationService is null)
        {
            SpkRotationDate = "Last rotated: Unknown";
            return;
        }

        try
        {
            var createdAt = _keyPublicationService.LocalBundle.SpkCreatedAt;
            SpkRotationDate = $"Last rotated: {createdAt.LocalDateTime:d}";
        }
        catch (InvalidOperationException)
        {
            SpkRotationDate = "Last rotated: Unknown";
        }
    }

    private async Task RegenerateIdentityKeyAsync()
    {
        if (_keyPublicationService is null) return;

        IsConfirmingRegenerate = false;
        IsRegenerating = true;
        try
        {
            await _keyPublicationService.RegenerateAndPublishAsync();
            RefreshSpkRotationDate();
        }
        finally
        {
            IsRegenerating = false;
        }
    }
}
