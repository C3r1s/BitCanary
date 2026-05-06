// Запуск Avalonia-приложения, DI-контейнер и инициализация клиента BitCanary.
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
        SetCurrentProcessExplicitAppUserModelID(AppId);

        ToastNotificationManagerCompat.OnActivated += toastArgs =>
        {
            var args = ToastArguments.Parse(toastArgs.Argument);
            if (!args.TryGetValue("chatId", out var chatIdStr)) return;
            if (!Guid.TryParse(chatIdStr, out var chatId)) return;

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

        }

        base.OnFrameworkInitializationCompleted();

        GlobalExceptionHandler.RegisterUiThreadHandler();
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
