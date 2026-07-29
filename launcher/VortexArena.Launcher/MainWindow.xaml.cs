using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;

namespace VortexArena.Launcher;

/// <summary>
/// Tek ekranlık operatör konsolu: <b>1 · Sunucu</b> (exe + mekan) → <b>2 · Bağlantı</b> (IP/port)
/// → <b>3 · Yönetim oyunu</b> (admin exe + başlat).
/// <para>
/// Sunucu <c>--venue &lt;mekan&gt;</c> ile başlatılır; mekan seçilmeden başlatma yoktur. Sebep:
/// mekansız açılışta sunucunun konsolu etkileşimli değilse alfabetik ilk mekan sessizce açılır
/// (bkz. <c>Program.SelectVenue</c>) ve operatör yanlış işletmenin arenalarını yönetir.
/// </para>
/// <para>
/// Yönetim oyunu <c>--server-ip</c>/<c>--server-port</c> ile başlatılır; <c>AppBoot</c> bunları
/// okur. Launcher başlattığı süreçlerle ağ üzerinden konuşmaz — protokole hiç girmez.
/// </para>
/// </summary>
public partial class MainWindow : Window
{
    private static readonly Regex DigitsOnly = new("^[0-9]+$");

    private LauncherConfig _config = new();
    private VenueCatalog _catalog = VenueCatalog.Empty;

    /// <summary>Alanları koddan doldururken <c>TextChanged</c>/<c>SelectionChanged</c>
    /// geri yazmasın diye.</summary>
    private bool _loading;

    private Process? _gameProcess;
    private Process? _serverProcess;

    public MainWindow()
    {
        InitializeComponent();
        LoadConfig();
    }

    // ─────────────────────────────────────────────────────────────── yükleme

    private void LoadConfig()
    {
        _loading = true;
        _config = LauncherConfig.Load();

        IpBox.Text = _config.ServerIp;
        PortBox.Text = _config.ServerPort.ToString(CultureInfo.InvariantCulture);
        ShowPath(ServerExeBox, _config.ServerExePath);
        ShowPath(AdminExeBox, _config.AdminExePath);
        _loading = false;

        RefreshVenues();
        UpdateWarnings();
    }

    private static void ShowPath(TextBox box, string path)
    {
        box.Text = path.Length == 0 ? "Seçilmedi" : path;
        box.ToolTip = path.Length == 0 ? null : path;
    }

    private void UpdateWarnings()
    {
        ServerExeWarning.Visibility = _config.ServerExePath.Length > 0 && !_config.ServerExeExists
            ? Visibility.Visible
            : Visibility.Collapsed;

        AdminExeWarning.Visibility = _config.AdminExePath.Length > 0 && !_config.AdminExeExists
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        Persist();
        base.OnClosing(e);
    }

    private void Persist()
    {
        try
        {
            _config.Save();
        }
        catch (Exception ex)
        {
            SetStatus($"Ayarlar kaydedilemedi: {ex.Message}", isError: true);
        }
    }

    // ───────────────────────────────────────────────────────────────── mekan

    /// <summary>
    /// Mekan listesini sunucunun kendi <c>config\maps.json</c>'undan tazeler. Liste okunamazsa
    /// elle yazma kutusuna düşülür — launcher içinde ikinci bir mekan kataloğu tutulmaz.
    /// </summary>
    private void RefreshVenues()
    {
        _loading = true;
        _catalog = VenueCatalog.ForServerExe(_config.ServerExePath);

        if (_catalog.Venues.Count > 0)
        {
            VenueList.Visibility = Visibility.Visible;
            VenueManualBox.Visibility = Visibility.Collapsed;
            VenueList.ItemsSource = _catalog.Venues;

            var current = _config.Venue.Trim();
            var match = _catalog.Venues.FirstOrDefault(
                v => string.Equals(v.Name, current, StringComparison.OrdinalIgnoreCase));

            // Tek mekan varsa seçim zaten belirlidir — operatörü tıklatmaya zorlamayız.
            if (match == null && current.Length == 0 && _catalog.Venues.Count == 1)
            {
                match = _catalog.Venues[0];
                _config.Venue = match.Name;
            }

            VenueList.SelectedItem = match;

            var source = _catalog.SourcePath ?? "maps.json";
            VenueHint.Text = match == null && current.Length > 0
                ? $"Kayıtlı mekan '{current}' bu listede yok — yeniden seçin.  ({source})"
                : $"{_catalog.Venues.Count} mekan · {source}";
        }
        else
        {
            VenueList.Visibility = Visibility.Collapsed;
            VenueList.ItemsSource = null;
            VenueManualBox.Visibility = Visibility.Visible;
            VenueManualBox.Text = _config.Venue;
            VenueHint.Text = _catalog.Problem ?? "Mekan listesi okunamadı — adı elle yazın.";
        }

        _loading = false;
    }

    private void RefreshVenuesButton_Click(object sender, RoutedEventArgs e)
    {
        RefreshVenues();
        SetStatus(_catalog.Problem ?? $"Mekan listesi tazelendi ({_catalog.Venues.Count} mekan).",
            isError: _catalog.Problem != null);
    }

    private void VenueList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        if (VenueList.SelectedItem is VenueInfo venue)
        {
            _config.Venue = venue.Name;
            Persist();
        }
    }

    private void VenueManualBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading) return;
        _config.Venue = VenueManualBox.Text;
    }

    // ───────────────────────────────────────────────────────────── bağlantı

    private void IpBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading) return;
        _config.ServerIp = IpBox.Text;
    }

    private void PortBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading) return;
        _config.ServerPort = int.TryParse(PortBox.Text.Trim(), out var port)
            ? port
            : LauncherConfig.DefaultPort;
    }

    private void PortBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        e.Handled = !DigitsOnly.IsMatch(e.Text);
    }

    // ─────────────────────────────────────────────────────────── dosya seçme

    private void BrowseServerButton_Click(object sender, RoutedEventArgs e)
    {
        var path = PickExe("Sunucu exe'sini seçin", _config.ServerExePath);
        if (path == null) return;

        _config.ServerExePath = path;
        ShowPath(ServerExeBox, path);
        UpdateWarnings();
        Persist();
        RefreshVenues();
        SetStatus(_catalog.Problem ?? "Sunucu exe seçildi, mekan listesi okundu.",
            isError: _catalog.Problem != null);
    }

    private void BrowseAdminButton_Click(object sender, RoutedEventArgs e)
    {
        var path = PickExe("Admin (yönetim) exe'sini seçin", _config.AdminExePath);
        if (path == null) return;

        _config.AdminExePath = path;
        ShowPath(AdminExeBox, path);
        UpdateWarnings();
        Persist();
        SetStatus("Admin exe seçildi.");
    }

    private static string? PickExe(string title, string current)
    {
        var dialog = new OpenFileDialog
        {
            Title = title,
            Filter = "Uygulama (*.exe)|*.exe|Tüm dosyalar (*.*)|*.*",
            CheckFileExists = true,
        };

        try
        {
            var dir = current.Length > 0 ? Path.GetDirectoryName(current) : null;
            if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) dialog.InitialDirectory = dir;
        }
        catch (Exception)
        {
            // Bozuk yol: varsayılan klasörle açılsın, seçim engellenmesin.
        }

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    private void ServerLogButton_Click(object sender, RoutedEventArgs e)
    {
        var dir = _config.ServerExePath.Length > 0
            ? Path.GetDirectoryName(_config.ServerExePath)
            : null;

        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
        {
            SetStatus("Sunucu klasörü bilinmiyor — önce sunucu exe'sini seçin.", isError: true);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo { FileName = dir, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            SetStatus($"Klasör açılamadı: {ex.Message}", isError: true);
        }
    }

    // ────────────────────────────────────────────────────── sunucuyu başlat

    private void StartServerButton_Click(object sender, RoutedEventArgs e)
    {
        Persist();

        if (_serverProcess is { HasExited: false })
        {
            SetStatus($"Sunucu bu launcher'dan zaten çalışıyor (PID {_serverProcess.Id}). " +
                      "Kapatmak için sunucu penceresinde Ctrl+C.", isError: true);
            return;
        }

        var problem = _config.ValidateServer(_catalog.Names);
        if (problem != null)
        {
            SetStatus(problem, isError: true);
            return;
        }

        var venue = _catalog.Venues.FirstOrDefault(
            v => string.Equals(v.Name, _config.Venue.Trim(), StringComparison.OrdinalIgnoreCase));
        if (venue is { HasLobby: false })
        {
            SetStatus($"'{venue.Name}' mekanında lobi haritası yok — sunucu açık sahne çözemeyip " +
                      "2 çıkış koduyla kapanır. O mekanın Lobby kutusunu ekleyip Export Server " +
                      "Config çalıştırın.", isError: true);
            return;
        }

        // Sunucu bir konsol uygulamasıdır ve KENDİ penceresinde açılmalıdır: operatör durum
        // satırlarını görsün, Ctrl+C ile temiz kapatabilsin. Bu yüzden çıktı YÖNLENDİRİLMEZ —
        // okunmayan bir boru çocuk süreci kilitler.
        var info = new ProcessStartInfo
        {
            FileName = _config.ServerExePath,
            WorkingDirectory = Path.GetDirectoryName(_config.ServerExePath) ?? "",
            UseShellExecute = false,
            CreateNoWindow = false,
        };
        foreach (var arg in _config.ServerArguments) info.ArgumentList.Add(arg);

        try
        {
            var process = Process.Start(info);
            if (process == null)
            {
                SetStatus("Sunucu başlatılamadı (süreç oluşmadı).", isError: true);
                return;
            }

            _serverProcess = process;
            process.EnableRaisingEvents = true;
            process.Exited += (_, _) => Dispatcher.Invoke(() => OnServerExited(process));

            ServerRunState.Text = $"Çalışıyor — PID {process.Id} · mekan '{_config.Venue.Trim()}'";
            SetStatus($"Sunucu başlatıldı: {LauncherConfig.ArgVenue} {_config.Venue.Trim()} " +
                      $"(PID {process.Id}). Kapatmak için sunucu penceresinde Ctrl+C.");
        }
        catch (Exception ex)
        {
            SetStatus($"Sunucu başlatılamadı: {ex.Message}", isError: true);
        }
    }

    private void OnServerExited(Process process)
    {
        if (!ReferenceEquals(process, _serverProcess)) return;

        var code = process.ExitCode;
        _serverProcess = null;
        ServerRunState.Text = "";

        // 2 = §11 fail-fast: açık sahne çözülemedi (lobi haritası yok / maps.json eksik).
        SetStatus(code switch
        {
            0 => "Sunucu kapandı.",
            2 => "Sunucu açılış doğrulamasında durdu (çıkış kodu 2): açık sahne çözülemedi. " +
                 "Seçilen mekanda lobi haritası var mı? Export Server Config çalıştırıldı mı?",
            _ => $"Sunucu {code} çıkış koduyla kapandı.",
        }, isError: code != 0);
    }

    // ─────────────────────────────────────────────────────── oyunu başlat

    private void StartGameButton_Click(object sender, RoutedEventArgs e)
    {
        Persist();

        if (_gameProcess is { HasExited: false })
        {
            SetStatus($"Oyun zaten çalışıyor (PID {_gameProcess.Id}).", isError: true);
            return;
        }

        var problem = _config.Validate();
        if (problem != null)
        {
            SetStatus(problem, isError: true);
            return;
        }

        // Unity GUI uygulamasıdır ve kendi log dosyasına yazar: çıktı yönlendirilmez
        // (okunmayan boru çocuk süreci kilitler).
        var info = new ProcessStartInfo
        {
            FileName = _config.AdminExePath,
            WorkingDirectory = Path.GetDirectoryName(_config.AdminExePath) ?? "",
            UseShellExecute = false,
        };
        foreach (var arg in _config.GameArguments) info.ArgumentList.Add(arg);

        try
        {
            var process = Process.Start(info);
            if (process == null)
            {
                SetStatus("Oyun başlatılamadı (süreç oluşmadı).", isError: true);
                return;
            }

            _gameProcess = process;
            process.EnableRaisingEvents = true;
            process.Exited += (_, _) => Dispatcher.Invoke(() => OnGameExited(process));

            SetGameRunning(true, process.Id);
            SetStatus($"Başlatıldı — PID {process.Id} · {_config.ServerIp.Trim()}:{_config.ServerPort}");
        }
        catch (Exception ex)
        {
            SetStatus($"Başlatılamadı: {ex.Message}", isError: true);
        }
    }

    private void StopGameButton_Click(object sender, RoutedEventArgs e)
    {
        if (_gameProcess is not { HasExited: false })
        {
            SetStatus("Bu launcher'dan başlatılmış oyun yok.", isError: true);
            return;
        }

        try
        {
            _gameProcess.Kill(entireProcessTree: true);
            SetStatus("Kapatma sinyali gönderildi.");
        }
        catch (Exception ex)
        {
            SetStatus($"Kapatılamadı: {ex.Message}", isError: true);
        }
    }

    private void OnGameExited(Process process)
    {
        if (!ReferenceEquals(process, _gameProcess)) return;

        var code = process.ExitCode;
        _gameProcess = null;
        SetGameRunning(false, 0);
        SetStatus(code == 0 ? "Oyun kapandı." : $"Oyun {code} çıkış koduyla kapandı.",
            isError: code != 0);
    }

    private void SetGameRunning(bool running, int pid)
    {
        StartGameButton.IsEnabled = !running;
        StopGameButton.IsEnabled = running;
        StartGameLabel.Text = running ? $"Çalışıyor (PID {pid})" : "Yönetimi Başlat";
    }

    // ────────────────────────────────────────────────────────────── durum

    private void SetStatus(string message, bool isError = false)
    {
        StatusBanner.Visibility = Visibility.Visible;
        StatusBanner.Background = (System.Windows.Media.Brush)FindResource(
            isError ? "ErrorContainerBrush" : "SurfaceHighBrush");
        StatusIcon.Text = isError ? "\uE7BA" : "\uE946"; // Segoe: uyari / bilgi
        StatusIcon.Foreground = (System.Windows.Media.Brush)FindResource(
            isError ? "ErrorBrush" : "OnSurfaceVariantBrush");
        StatusText.Foreground = (System.Windows.Media.Brush)FindResource(
            isError ? "ErrorBrush" : "OnSurfaceBrush");
        StatusText.Text = message;
    }
}
