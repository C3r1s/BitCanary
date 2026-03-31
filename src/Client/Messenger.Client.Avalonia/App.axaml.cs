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
