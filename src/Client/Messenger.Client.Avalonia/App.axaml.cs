using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Messenger.Client.Avalonia.Services;
using Messenger.Client.Avalonia.ViewModels;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
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

        var localAppData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Messenger.Client.Avalonia");
        Directory.CreateDirectory(localAppData);

        services.AddDataProtection()
            .SetApplicationName("Messenger.Client.Avalonia")
            .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(localAppData, "dp-keys")))
            .ProtectKeysWithDpapi();  // no-arg = CurrentUser scope

        // ── Core services ──────────────────────────────────────────────────
        services.AddSingleton<IKeyStore, DpapiKeyStore>();
        services.AddSingleton<IClientSessionService, ClientSessionService>();
        services.AddSingleton<ILocalCacheService, LocalCacheService>();
        services.AddSingleton<IThemeService, ThemeService>();
        services.AddSingleton<IEncryptionService, LocalEnvelopeEncryptionService>();
        services.AddSingleton<IMessengerApiClient, MessengerApiClient>();
        services.AddSingleton<IRealtimeClient, RealtimeClient>();

        // ── Storage ────────────────────────────────────────────────────────
        services.AddSingleton<DatabaseService>();
        services.AddSingleton<ILocalMessageRepository>(sp =>
        {
            var dbService = sp.GetRequiredService<DatabaseService>();
            var conn = dbService.OpenAsync(cancellationToken: CancellationToken.None)
                                .GetAwaiter().GetResult();
            return new LocalMessageRepository(conn);
        });

        // ── Migration ──────────────────────────────────────────────────────
        services.AddSingleton<StartupMigrationService>();

        // ── ViewModels ─────────────────────────────────────────────────────
        services.AddSingleton<MainWindowViewModel>();

        var provider = services.BuildServiceProvider();

        // Run startup migration synchronously before the app's main window opens
        provider.GetRequiredService<StartupMigrationService>()
            .RunAsync(CancellationToken.None).GetAwaiter().GetResult();
        return provider;
    }
}
