using Messenger.Shared.Contracts;

namespace Messenger.Client.Avalonia.Services;

public interface IThemeService
{
    ThemePreference CurrentTheme { get; }
    void Apply(ThemePreference themePreference);
}
