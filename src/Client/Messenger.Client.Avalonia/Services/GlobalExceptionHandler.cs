// Сервис клиента BitCanary: сеть, кэш, медиа — «GlobalExceptionHandler».
using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Threading;
using Messenger.Client.Avalonia.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Messenger.Client.Avalonia.Services;

public static class GlobalExceptionHandler
{
    private const string LogDirectoryPath = @"E:\Programming\CsharpProj\BitCanary\storage\logs";
    private static readonly string LogFilePath = Path.Combine(LogDirectoryPath, "desktop-app.log");

    public static void RegisterProcessLevelHandlers()
    {
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandled;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        Directory.CreateDirectory(Path.GetDirectoryName(LogFilePath)!);
    }

    public static void RegisterUiThreadHandler()
    {
        Dispatcher.UIThread.UnhandledException += OnUiThreadUnhandled;
    }

    private static void OnAppDomainUnhandled(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            Show("AppDomain.UnhandledException", ex, isTerminating: e.IsTerminating);
        }
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        e.SetObserved();
        Show("TaskScheduler.UnobservedTaskException", e.Exception);
    }

    private static void OnUiThreadUnhandled(object? sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        Show("Dispatcher.UIThread.UnhandledException", e.Exception);
    }

    private static void Show(string source, Exception ex, bool isTerminating = false)
    {
        try
        {
            var level = isTerminating ? "FATAL" : "ERROR";
            Console.Error.WriteLine($"[{level}] {source}\n{ex}\n");

            File.AppendAllText(LogFilePath,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level}] {source}: {ex}\n\n");

            var vm = App.Services.GetService<MainWindowViewModel>();
            vm?.ShowFatalError(source, ex);
        }
        catch
        {
        }
    }
}
