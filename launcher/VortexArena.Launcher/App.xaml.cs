using System.Windows;
using System.Windows.Threading;

namespace VortexArena.Launcher;

/// <summary>
/// VortexArena operator launcher (Windows desktop, WPF).
/// <para>
/// Two jobs: starting <b>the server with the right venue</b> (<c>--venue</c>) and <b>the admin game
/// with the right address</b> (<c>--server-ip</c>/<c>--server-port</c>). Never touches the protocol —
/// it does not talk to the processes it starts.
/// </para>
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // Nobody on site is watching Visual Studio: an unhandled error must show a readable box
        // instead of closing silently, and the launcher must stay up.
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        base.OnStartup(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(
            $"Beklenmeyen hata:\n\n{e.Exception.Message}",
            "VortexArena Launcher",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }
}
