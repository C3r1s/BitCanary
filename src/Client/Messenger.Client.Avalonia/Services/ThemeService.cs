using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;
using Messenger.Shared.Contracts;

namespace Messenger.Client.Avalonia.Services;

public sealed class ThemeService : IThemeService
{
    private const string SentinelKey = "IsTerminalTheme";

    public ThemePreference CurrentTheme { get; private set; } = ThemePreference.System;

    public void Apply(ThemePreference themePreference)
    {
        CurrentTheme = themePreference;

        if (Application.Current is null)
        {
            return;
        }

        if (themePreference != ThemePreference.Terminal)
        {
            // Remove any existing terminal dictionaries
            RemoveTerminalDictionaries();
            // Restore default font
            RestoreDefaultFont();
        }

        Application.Current.RequestedThemeVariant = themePreference switch
        {
            ThemePreference.Light => ThemeVariant.Light,
            ThemePreference.Dark => ThemeVariant.Dark,
            ThemePreference.Terminal => ThemeVariant.Dark,
            _ => ThemeVariant.Default
        };
    }

    /// <summary>
    /// Loads and applies one of the three Terminal color ResourceDictionaries.
    /// Call this when Terminal theme is active and the user picks a sub-scheme.
    /// </summary>
    public void ApplyTerminalScheme(TerminalColorScheme scheme)
    {
        if (Application.Current is null) return;

        RemoveTerminalDictionaries();

        var schemeName = scheme switch
        {
            TerminalColorScheme.AmberTerminal => "TerminalAmber",
            TerminalColorScheme.CyberpunkCyan => "TerminalCyan",
            _ => "TerminalGreen"
        };

        var uri = new Uri($"avares://Messenger.Client.Avalonia/Assets/Themes/{schemeName}.axaml");
        var dict = (ResourceDictionary)AvaloniaXamlLoader.Load(uri);
        Application.Current.Resources.MergedDictionaries.Add(dict);

        // Override font family to monospace
        Application.Current.Resources["ContentControlThemeFontFamily"] =
            new FontFamily("Fixedsys, Terminal, 'Lucida Console', 'Courier New', monospace");
    }

    // ─────────────────────────────────────────────────────────────────────────

    private static void RemoveTerminalDictionaries()
    {
        if (Application.Current is null) return;

        var toRemove = Application.Current.Resources.MergedDictionaries
            .OfType<ResourceDictionary>()
            .Where(d => d.TryGetValue(SentinelKey, out _))
            .ToList();

        foreach (var d in toRemove)
        {
            Application.Current.Resources.MergedDictionaries.Remove(d);
        }
    }

    private static void RestoreDefaultFont()
    {
        if (Application.Current is null) return;

        // Remove the overridden font key so the default Fluent theme font takes over again
        if (Application.Current.Resources.ContainsKey("ContentControlThemeFontFamily"))
        {
            Application.Current.Resources.Remove("ContentControlThemeFontFamily");
        }
    }
}
