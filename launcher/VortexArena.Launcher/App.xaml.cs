using System.Windows;
using System.Windows.Threading;

namespace VortexArena.Launcher;

/// <summary>
/// VortexArena operatör launcher'ı (Windows masaüstü, WPF).
/// <para>
/// İki iş yapar: <b>sunucuyu doğru mekanla</b> (<c>--venue</c>) ve <b>yönetim oyununu doğru
/// adresle</b> (<c>--server-ip</c>/<c>--server-port</c>) başlatmak. Protokole hiç girmez —
/// başlattığı süreçlerle ağ üzerinden konuşmaz.
/// </para>
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // İşletmede kimse Visual Studio'ya bakmıyor: yakalanmamış bir hata sessiz kapanma yerine
        // okunabilir bir kutu göstersin, launcher ayakta kalsın.
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
