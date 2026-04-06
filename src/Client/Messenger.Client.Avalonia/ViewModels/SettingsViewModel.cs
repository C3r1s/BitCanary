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
    private bool _useCompactMode;

    [ObservableProperty]
    private bool _enableCustomEmoji = true;

    // ── Encryption section ────────────────────────────────────────────────────

    [ObservableProperty]
    private string _spkRotationDate = "Last rotated: Unknown";

    [ObservableProperty]
    private bool _canShowSafetyNumber;

    [ObservableProperty]
    private bool _isRegenerating;

    /// <summary>
    /// True when the regeneration confirmation overlay is visible.
    /// </summary>
    [ObservableProperty]
    private bool _isConfirmingRegenerate;

    /// <summary>Relay from MainWindowViewModel so Settings can trigger safety number view.</summary>
    public IRelayCommand? ShowSafetyNumberCommand { get; set; }

    public IAsyncRelayCommand RegenerateIdentityKeyCommand { get; }

    public IRelayCommand ShowRegenerateConfirmCommand { get; }

    public IRelayCommand CancelRegenerateCommand { get; }

    // ── Theme section ─────────────────────────────────────────────────────────

    public ObservableCollection<ThemeOption> ThemeOptions { get; } =
    [
        new ThemeOption(ThemePreference.System, "System"),
        new ThemeOption(ThemePreference.Light, "Light"),
        new ThemeOption(ThemePreference.Dark, "Dark"),
        new ThemeOption(ThemePreference.Terminal, "Terminal (Hacker)")
    ];

    // ── Terminal sub-scheme ───────────────────────────────────────────────────

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

    // ─────────────────────────────────────────────────────────────────────────

    public SettingsViewModel(
        Func<ThemePreference, Task> changeThemeAsync,
        KeyPublicationService? keyPublicationService = null,
        Action<Messenger.Shared.Contracts.TerminalColorScheme>? applyTerminalScheme = null)
    {
        _changeThemeAsync = changeThemeAsync;
        _keyPublicationService = keyPublicationService;
        _applyTerminalScheme = applyTerminalScheme;
        SelectedThemeOption = ThemeOptions[0];
        SelectedTerminalScheme = TerminalSchemeOptions[0];

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
        if (value is not null && IsTerminalThemeSelected)
        {
            _applyTerminalScheme?.Invoke(value.Value);
        }
    }

    /// <summary>
    /// Refreshes the SPK rotation date displayed in the Encryption section.
    /// Call this after key bundle is loaded.
    /// </summary>
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
