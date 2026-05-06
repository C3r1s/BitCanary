// Сервис клиента BitCanary: сеть, кэш, медиа — «ThemeService».
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Messenger.Shared.Contracts;

namespace Messenger.Client.Avalonia.Services;

public sealed class ThemeService : IThemeService
{
    private const string SentinelKey = "IsTerminalTheme";

    public ThemePreference CurrentTheme { get; private set; } = ThemePreference.System;

    public void Apply(ThemePreference themePreference)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => Apply(themePreference));
            return;
        }

        CurrentTheme = themePreference;

        if (Application.Current is null)
        {
            return;
        }

        if (themePreference != ThemePreference.Terminal)
        {
            RemoveTerminalDictionaries();
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

    public void ApplyTerminalScheme(TerminalColorScheme scheme)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => ApplyTerminalScheme(scheme));
            return;
        }

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

        foreach (var key in dict.Keys)
        {
            if (dict.TryGetResource(key, null, out var value))
            {
                Application.Current.Resources[key] = value;
            }
        }
    }


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

        if (Application.Current.Resources.ContainsKey("ContentControlThemeFontFamily"))
        {
            Application.Current.Resources.Remove("ContentControlThemeFontFamily");
        }
    }
}
