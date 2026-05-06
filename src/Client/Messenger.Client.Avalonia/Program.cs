// Точка входа десктоп-клиента BitCanary на Avalonia.
using Avalonia;
using System;
using Messenger.Client.Avalonia.Services;

namespace Messenger.Client.Avalonia;

class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        GlobalExceptionHandler.RegisterProcessLevelHandlers();

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
