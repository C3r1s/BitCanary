using Messenger.Client.Avalonia.Services;
using Messenger.Client.Avalonia.Services.Crypto;
using Messenger.Client.Avalonia.ViewModels;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Messenger.Client.Avalonia;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddMessengerClientServices(this IServiceCollection services)
    {
        var localAppData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Messenger.Client.Avalonia");
        Directory.CreateDirectory(localAppData);

        services.AddDataProtection()
            .SetApplicationName("Messenger.Client.Avalonia")
            .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(localAppData, "dp-keys")))
            .ProtectKeysWithDpapi();  // no-arg = CurrentUser scope

        services.AddLogging();

        // ── Core services ──────────────────────────────────────────────────
        services.AddSingleton<IKeyStore, DpapiKeyStore>();
        services.AddSingleton<IClientSessionService, ClientSessionService>();
        services.AddSingleton<ILocalCacheService, LocalCacheService>();
        services.AddSingleton<IThemeService, ThemeService>();
        services.AddSingleton<IMessengerApiClient, MessengerApiClient>();
        services.AddSingleton<IRealtimeClient, RealtimeClient>();

        // ── Storage ────────────────────────────────────────────────────────
        services.AddSingleton<DatabaseService>();
        services.AddSingleton<SqliteConnection>(sp =>
        {
            var dbService = sp.GetRequiredService<DatabaseService>();
            // Task.Run escapes the Avalonia SynchronizationContext so File I/O inside
            // OpenAsync doesn't deadlock when resolved lazily on the UI thread.
            return Task.Run(() => dbService.OpenAsync(cancellationToken: CancellationToken.None))
                       .GetAwaiter().GetResult();
        });
        services.AddSingleton<ILocalMessageRepository>(sp =>
            new LocalMessageRepository(sp.GetRequiredService<SqliteConnection>()));
        services.AddSingleton<IRatchetSessionRepository>(sp =>
            new RatchetSessionRepository(sp.GetRequiredService<SqliteConnection>()));

        // ── Crypto services ────────────────────────────────────────────────
        services.AddSingleton<IIdentityKeyChangeDetector, IdentityKeyChangeDetector>();
        services.AddSingleton<ISafetyNumberService, SafetyNumberService>();
        services.AddSingleton<IX3DHService, X3DHService>();
        services.AddSingleton<IDoubleRatchetService, DoubleRatchetService>();
        services.AddSingleton<ISessionManager, SessionManager>();
        services.AddSingleton<LocalEnvelopeEncryptionService>();  // retained as legacy decrypt delegate
        services.AddSingleton<KeyPublicationService>();
        services.AddSingleton<IEncryptionService, SignalProtocolEncryptionService>();

        // ── Migration ──────────────────────────────────────────────────────
        services.AddSingleton<StartupMigrationService>();

        // ── ViewModels ─────────────────────────────────────────────────────
        services.AddSingleton<MainWindowViewModel>();

        return services;
    }
}
