using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Messenger.Client.Avalonia.Services;
using Messenger.Client.Avalonia.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Toolkit.Uwp.Notifications;

namespace Messenger.Client.Avalonia;

public partial class App : Application
{
    public static IServiceProvider Services { get; } = BuildServices();

    private const string AppId = "Messenger.Client";

    [DllImport("shell32.dll", SetLastError = true)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(
        [MarshalAs(UnmanagedType.LPWStr)] string appId);

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // D-02 (CONTEXT.md): Must be first — before any toast API access.
        SetCurrentProcessExplicitAppUserModelID(AppId);

        // <!-- User decision: Using ToastNotificationManagerCompat.OnActivated instead of
        // NotificationActivator COM class per user confirmation — achieves identical D-03 intent
        // (chatId parsing + NavigateToChatAsync) without deprecated API -->
        // Subscribe before window opens (RESEARCH.md Pitfall 2).
        ToastNotificationManagerCompat.OnActivated += toastArgs =>
        {
            var args = ToastArguments.Parse(toastArgs.Argument);
            if (!args.TryGetValue("chatId", out var chatIdStr)) return;
            if (!Guid.TryParse(chatIdStr, out var chatId)) return;

            // OnActivated fires on a background thread — marshal to UI thread (RESEARCH.md Pattern 3).
            Dispatcher.UIThread.Post(() =>
            {
                var vm = Services.GetRequiredService<MainWindowViewModel>();
                vm.NavigateToChatAsync(chatId);
            });
        };

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var mainWindowViewModel = Services.GetRequiredService<MainWindowViewModel>();
            var mainWindow = new MainWindow
            {
                DataContext = mainWindowViewModel
            };

            desktop.MainWindow = mainWindow;

            RunMigration(mainWindowViewModel);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static IServiceProvider BuildServices()
    {
        var services = new ServiceCollection();
        services.AddMessengerClientServices();

        var provider = services.BuildServiceProvider();
        return provider;
    }

    private static void RunMigration(MainWindowViewModel mainWindowViewModel)
    {
        Task.Run(async () =>
        {
            mainWindowViewModel.IsMigrating = true;
            try
            {
                using var scope = Services.CreateScope();
                var migrationService = scope.ServiceProvider.GetRequiredService<StartupMigrationService>();
                await migrationService.RunAsync();
            }
            finally
            {
                mainWindowViewModel.IsMigrating = false;
            }
        });
    }
}
