using System.Windows;
using TizenLoaderBRDesktop.Helpers;

namespace TizenLoaderBRDesktop;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        base.OnStartup(e);
    }

    private void OnDispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        HandleFatalException(e.Exception);
        e.Handled = true;
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            HandleFatalException(exception);
        }
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        HandleFatalException(e.Exception);
        e.SetObserved();
    }

    private static void HandleFatalException(Exception exception)
    {
        try
        {
            AppPaths.EnsureBaseFolders();
            File.AppendAllText(AppPaths.LogsPath, $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}] FATAL: {exception}\n");
        }
        catch
        {
        }

        MessageBox.Show(exception.ToString(), "Tizen Loader BR Desktop - erro", MessageBoxButton.OK, MessageBoxImage.Error);
    }
}
