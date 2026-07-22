using System.Windows;
using System.Windows.Threading;
using AjazzKeyboard.Services;
using Wpf.Ui.Appearance;

namespace AjazzKeyboard;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ApplicationThemeManager.Apply(ApplicationTheme.Dark);

        // Catch-all logging: CommunityToolkit.Mvvm's AsyncRelayCommand swallows
        // exceptions from async [RelayCommand] methods by default (nothing rethrows
        // to the UI thread unless something observes ExecutionTask) — a loop like
        // ShowGridNumbers could throw partway through and just silently stop with
        // zero trace. These hooks make sure that can never happen unlogged again,
        // for command exceptions and for anything else (background threads, timers).
        DispatcherUnhandledException += (_, args) =>
        {
            Log.Write($"App.DispatcherUnhandledException: {args.Exception}");
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            Log.Write($"App.AppDomain.UnhandledException (IsTerminating={args.IsTerminating}): {args.ExceptionObject}");
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Write($"App.TaskScheduler.UnobservedTaskException: {args.Exception}");
            args.SetObserved();
        };

        Log.Write("App: global exception handlers registered.");
    }
}
