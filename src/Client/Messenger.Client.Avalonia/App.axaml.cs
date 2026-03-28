using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Messenger.Client.Avalonia.Services;
using Messenger.Client.Avalonia.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Messenger.Client.Avalonia;

public partial class App : Application
{
    public static IServiceProvider Services { get; } = BuildServices();

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var mainWindow = new MainWindow
            {
                DataContext = Services.GetRequiredService<MainWindowViewModel>()
            };

            desktop.MainWindow = mainWindow;
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static IServiceProvider BuildServices()
    {
        var services = new ServiceCollection();

        services.AddSingleton<IClientSessionService, ClientSessionService>();
        services.AddSingleton<ILocalCacheService, LocalCacheService>();
        services.AddSingleton<IThemeService, ThemeService>();
        services.AddSingleton<IEncryptionService, LocalEnvelopeEncryptionService>();
        services.AddSingleton<IMessengerApiClient, MessengerApiClient>();
        services.AddSingleton<IRealtimeClient, RealtimeClient>();
        services.AddSingleton<MainWindowViewModel>();

        return services.BuildServiceProvider();
    }
}
