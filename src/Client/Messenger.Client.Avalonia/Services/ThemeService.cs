using Avalonia;
using Avalonia.Styling;
using Messenger.Shared.Contracts;

namespace Messenger.Client.Avalonia.Services;

public sealed class ThemeService : IThemeService
{
    public ThemePreference CurrentTheme { get; private set; } = ThemePreference.System;

    public void Apply(ThemePreference themePreference)
    {
        CurrentTheme = themePreference;

        if (Application.Current is null)
        {
            return;
        }

        Application.Current.RequestedThemeVariant = themePreference switch
        {
            ThemePreference.Light => ThemeVariant.Light,
            ThemePreference.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default
        };
    }
}
