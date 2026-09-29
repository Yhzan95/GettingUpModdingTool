using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;

namespace GettingUpModTool;

public partial class App : Application
{
    private static readonly object CrashLogLock = new();

    protected override async void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += App_DispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
        base.OnStartup(e);

        // Splash texts should already be in the user's language.
        LocalizationService.SetLanguage(GettingUpModTool.Core.Game.AppSettingsStore.Load().Language);
        var splash = new SplashWindow();
        bool startupDone = false;
        splash.Closed += (_, _) =>
        {
            // Alt+F4 on the splash means "quit", not "open the app anyway".
            if (!startupDone)
                Shutdown();
        };
        splash.Show();
        LocalizationService.Apply(splash);

        // The main window is built and filled while hidden, then swapped in.
        // ShutdownMode stays explicit until then so closing the splash doesn't end the app.
        var main = new MainWindow();
        try
        {
            await main.InitializeAsync(splash.Progress);
            await splash.CompleteAsync(LocalizationService.T("Prêt."));
        }
        catch (Exception ex)
        {
            // A failed startup step must not leave the user stuck on the splash screen.
            WriteCrashLog("Startup", ex);
        }

        if (!splash.IsVisible)
            return; // closed during startup: Shutdown() is already under way

        startupDone = true;
        MainWindow = main;
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        main.Show();
        main.Activate();
        splash.Close();
    }

    private static string CrashLogPath
    {
        get
        {
            string folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "GettingUpModTool");
            Directory.CreateDirectory(folder);
            return Path.Combine(folder, "crash.log");
        }
    }

    private static void WriteCrashLog(string source, Exception exception)
    {
        try
        {
            lock (CrashLogLock)
            {
                var text = new StringBuilder()
                    .AppendLine("============================================================")
                    .AppendLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}  {source}")
                    .AppendLine($"OS: {Environment.OSVersion}")
                    .AppendLine($"Runtime: {Environment.Version}")
                    .AppendLine(exception.ToString())
                    .AppendLine()
                    .ToString();
                File.AppendAllText(CrashLogPath, text, Encoding.UTF8);
            }
        }
        catch (Exception logException)
        {
            
            Trace.WriteLine($"[GettingUpModTool] Crash logger failure: {logException}");
        }
    }

    private static void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        WriteCrashLog("DispatcherUnhandledException", e.Exception);

        
        
        try
        {
            MessageBox.Show(
                $"Getting Up Mod Tool encountered an unexpected error.\n\n" +
                $"A crash log was written to:\n{CrashLogPath}\n\n" +
                $"{e.Exception.GetType().Name}: {e.Exception.Message}",
                "Getting Up Mod Tool",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        catch (Exception dialogException)
        {
            Trace.WriteLine($"[GettingUpModTool] Impossible d'afficher le message de crash: {dialogException}");
        }
    }

    private static void CurrentDomain_UnhandledException(object? sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
            WriteCrashLog("AppDomain.UnhandledException", exception);
    }

    private static void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        WriteCrashLog("TaskScheduler.UnobservedTaskException", e.Exception);
    }
}
