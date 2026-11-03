using System.Windows;
using Sketchora.App.Services;

namespace Sketchora.App;

public partial class App : Application
{
    private Mutex? _instance;
    private AppController? _controller;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _instance = new Mutex(true, @"Local\Sketchora.Desktop", out var firstInstance);
        if (!firstInstance)
        {
            MessageBox.Show("Sketchora is already running. Open it from the system tray.", "Sketchora");
            Shutdown();
            return;
        }

        DispatcherUnhandledException += (_, args) =>
        {
            // Never leave a full-screen input layer behind after an unexpected UI error.
            _controller?.EmergencyHide();
            try
            {
                Directory.CreateDirectory(AppController.DataDirectory);
                File.AppendAllText(Path.Combine(AppController.DataDirectory, "error.log"),
                    $"{DateTimeOffset.Now:O} {args.Exception}\n");
            }
            catch (Exception logError) when (logError is IOException or UnauthorizedAccessException) { }
            MessageBox.Show("Sketchora encountered an error and released the screen. " +
                "The app will close. See the local error.log for details.", "Sketchora");
            args.Handled = true;
            Shutdown(1);
        };

        _controller = new AppController();
        _controller.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _controller?.Dispose();
        _instance?.Dispose();
        base.OnExit(e);
    }
}
