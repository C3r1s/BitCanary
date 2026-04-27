using System;
using System.Threading.Tasks;
using Avalonia.Threading;
using Messenger.Client.Avalonia.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Messenger.Client.Avalonia.Services;

/// <summary>
/// Phase 21 / STAB-01: routes every unhandled exception to a user-friendly modal
/// dialog instead of letting the process terminate.
///
/// Subscribes to:
///   - AppDomain.CurrentDomain.UnhandledException  (any thread, terminating event)
///   - TaskScheduler.UnobservedTaskException       (faulted Tasks never awaited)
///   - Dispatcher.UIThread.UnhandledException       (Avalonia UI thread)
///
/// All three handlers MARK the exception observed/handled where possible so the
/// process does not terminate. The user sees the fatal-error overlay; the app
/// continues running.
/// </summary>
public static class GlobalExceptionHandler
{
    /// <summary>
    /// Wire the two CLR-level handlers. Call from Program.Main BEFORE
    /// BuildAvaloniaApp so very-early failures are captured.
    /// </summary>
    public static void RegisterProcessLevelHandlers()
    {
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandled;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    /// <summary>
    /// Wire the Avalonia UI-thread handler. Call from
    /// App.OnFrameworkInitializationCompleted AFTER base.OnFramework... so the
    /// dispatcher exists.
    /// </summary>
    public static void RegisterUiThreadHandler()
    {
        Dispatcher.UIThread.UnhandledException += OnUiThreadUnhandled;
    }

    private static void OnAppDomainUnhandled(object sender, UnhandledExceptionEventArgs e)
    {
        // e.IsTerminating is true for fatal CLR-level failures we cannot stop;
        // for everything else we show the dialog and let the app keep running.
        if (e.ExceptionObject is Exception ex)
        {
            Show("AppDomain.UnhandledException", ex);
        }
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        // Mark observed so the finalizer does NOT escalate to process termination
        // on legacy <ThrowUnobservedTaskExceptions> runtimes.
        e.SetObserved();
        Show("TaskScheduler.UnobservedTaskException", e.Exception);
    }

    private static void OnUiThreadUnhandled(object? sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // CRITICAL: setting Handled = true keeps the dispatcher loop alive.
        // Without this, Avalonia tears down the UI thread and the window vanishes.
        e.Handled = true;
        Show("Dispatcher.UIThread.UnhandledException", e.Exception);
    }

    private static void Show(string source, Exception ex)
    {
        try
        {
            // Best-effort console trace so devs see something even if VM resolution fails.
            System.Diagnostics.Debug.WriteLine(
                $"[GlobalExceptionHandler] {source}: {ex.GetType().FullName}: {ex.Message}");

            var vm = App.Services.GetService<MainWindowViewModel>();
            vm?.ShowFatalError(source, ex);
        }
        catch
        {
            // Swallow — the handler itself MUST NOT throw, or we lose the safety net.
        }
    }
}
