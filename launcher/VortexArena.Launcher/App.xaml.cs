using System.Windows;
using System.Windows.Threading;
using VortexArena.Launcher.Infrastructure;
using VortexArena.Launcher.ViewModels;

namespace VortexArena.Launcher;

/// <summary>
/// VortexArena operator launcher (Windows desktop, WPF).
/// <para>
/// One window, four pages: the match server, the admin build, the headset APK versions and match
/// recording. The deployment layout under one root replaces every path setting.
/// </para>
/// </summary>
public partial class App : Application
{
    private readonly SingleInstanceGuard _guard = new();
    private MainViewModel? _model;

    protected override void OnStartup(StartupEventArgs e)
    {
        // Nobody on site is watching Visual Studio: an unhandled error must show a readable box
        // instead of closing silently, and the launcher must stay up.
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AsyncRelayCommand.UnhandledError = ex => Dialogs.Error($"Beklenmeyen hata:\n\n{ex.Message}");

        if (!_guard.TryAcquire())
        {
            // ⚠️ A copy from another folder must say so: silently raising the old window looks
            // like this copy opened, with the old root.
            var running = SingleInstanceGuard.RunningInstancePath();
            if (!SameFile(running, Environment.ProcessPath))
            {
                Dialogs.Info(running is null
                    ? "Başka bir launcher zaten açık; bu kopya açılmadı.\n\nBu kopyayı kullanmak için önce açık olanı kapatın."
                    : $"Launcher zaten açık:\n{running}\n\nBu kopya açılmadı. Bu kopyayı kullanmak için önce açık olanı kapatın.");
            }

            SingleInstanceGuard.SignalExistingInstance();
            Shutdown();
            return;
        }

        base.OnStartup(e);

        _model = new MainViewModel(e.Args);
        var window = new MainWindow(_model);
        MainWindow = window;

        _guard.ListenForActivation(() => Dispatcher.Invoke(() => NativeMethods.Activate(window)));

        window.Show();
        _model.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _model?.Dispose();
        _guard.Dispose();
        base.OnExit(e);
    }

    private static bool SameFile(string? a, string? b)
    {
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
        try
        {
            return string.Equals(
                System.IO.Path.GetFullPath(a), System.IO.Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Dialogs.Error($"Beklenmeyen hata:\n\n{e.Exception.Message}");
        e.Handled = true;
    }
}
