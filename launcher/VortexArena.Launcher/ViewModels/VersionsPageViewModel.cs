using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows.Input;
using VortexArena.Launcher.Infrastructure;
using VortexArena.Launcher.Services;

namespace VortexArena.Launcher.ViewModels;

/// <summary>
/// Versions page: the update server's APK list, what is on this PC and what is on the headset.
/// <para>⚠️ <b>Nothing is ever uninstalled on its own.</b> Each version has its own package id, so
/// installing one never touches another; the only uninstall path is the operator confirming a
/// signature conflict for that single version.</para>
/// </summary>
public sealed class VersionsPageViewModel : PageViewModel
{
    /// <summary>Tick spacing (shell ticks once a second).</summary>
    private const int DevicePollTicks = 2;

    private const int PackagePollTicks = 10;

    private const int ListRetryTicks = 30;

    private readonly Dictionary<int, VersionRowViewModel> _rowsByVersion = [];
    private readonly RelayCommand _refreshCommand;

    private IReadOnlyList<RemoteVersion>? _remote;
    private IReadOnlyList<int> _installed = [];
    private AdbDevice? _selectedDevice;
    private string _adbError = "";
    private string _listError = "";
    private bool _adbReady;
    private bool _installBusy;
    private bool _listBusy;
    private bool _tickBusy;
    private int _tick;
    private int _ticksSinceListError;

    public VersionsPageViewModel(LauncherContext context) : base(context)
    {
        _refreshCommand = new RelayCommand(() => _ = RefreshListAsync(), () => !_listBusy);
        OpenFolderCommand = new RelayCommand(OpenFolder);
    }

    public override string Title => "Versiyonlar";

    public override string Description =>
        "Oyun sürümlerini güncelleme sunucusundan indirip gözlüğe kurar. Her sürüm ayrı paket olarak " +
        "durur; kurulum başka sürümü silmez.";

    public ObservableCollection<VersionRowViewModel> Rows { get; } = [];

    public ObservableCollection<AdbDevice> Devices { get; } = [];

    public ICommand RefreshCommand => _refreshCommand;

    public ICommand OpenFolderCommand { get; }

    // --------------------------------------------------------------------- headset

    public AdbDevice? SelectedDevice
    {
        get => _selectedDevice;
        set
        {
            if (!Set(ref _selectedDevice, value)) return;

            if (value is not null)
            {
                Context.Settings.PreferredDeviceSerial = value.Serial;
                Context.SaveSettings();
            }

            _installed = [];
            RebuildRows();
            OnPropertyChanged(nameof(DeviceStateText));
            OnPropertyChanged(nameof(DeviceReady));
        }
    }

    public bool DeviceReady => _selectedDevice?.IsReady == true && !_installBusy;

    /// <summary>An authorized headset is attached (sidebar badge; ignores install busy-ness).</summary>
    public bool DeviceAttached => _selectedDevice?.IsReady == true;

    /// <summary>A download or install is in flight — the exit prompt warns about it.</summary>
    public bool IsWorking => _installBusy || Rows.Any(r => r.IsBusy);

    public bool HasManyDevices => Devices.Count > 1;

    public string DeviceStateText
    {
        get
        {
            if (_adbError.Length > 0) return _adbError;
            if (!_adbReady) return "adb hazırlanıyor…";
            if (Devices.Count == 0) return "Gözlük takılı değil";

            var device = _selectedDevice ?? Devices[0];
            return device.State switch
            {
                AdbDevice.StateUnauthorized =>
                    "İzin bekleniyor — gözlükte 'USB hata ayıklamaya izin ver'i onaylayın",
                AdbDevice.StateOffline => "Bağlantı kuruluyor",
                AdbDevice.StateReady => $"{device.Display} bağlı",
                _ => $"{device.Display} — {device.State}",
            };
        }
    }

    /// <summary>Warning strip text; empty hides the strip.</summary>
    public string ListError
    {
        get => _listError;
        private set
        {
            if (Set(ref _listError, value)) OnPropertyChanged(nameof(HasListError));
        }
    }

    public bool HasListError => _listError.Length > 0;

    // ------------------------------------------------------------------------ ticks

    /// <summary>
    /// ⚠️ Does NOT block the shell tick: an adb call can take seconds and the server status poll
    /// must keep running. The work continues on the dispatcher and the next tick is skipped while
    /// it is in flight.
    /// </summary>
    public override Task TickAsync()
    {
        if (_tickBusy) return Task.CompletedTask;

        _tickBusy = true;
        _ = RunTickAsync();
        return Task.CompletedTask;
    }

    private async Task RunTickAsync()
    {
        try
        {
            await PollAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Fail($"Gözlük sorgusu başarısız: {ex.Message}");
        }
        finally
        {
            _tickBusy = false;
        }
    }

    private async Task PollAsync()
    {
        _tick++;

        // The APK list does not depend on adb: a headset-less PC must still show what is published.
        if (_tick == 1) await RefreshListAsync().ConfigureAwait(true);

        if (!_adbReady && _adbError.Length == 0)
        {
            var error = await Context.Adb.EnsureReadyAsync(CancellationToken.None).ConfigureAwait(true);
            if (error is null) _adbReady = true; else _adbError = error;
            OnPropertyChanged(nameof(DeviceStateText));
        }

        if (_adbReady && _tick % DevicePollTicks == 0) await PollDevicesAsync().ConfigureAwait(true);

        if (_adbReady && DeviceReady && _tick % PackagePollTicks == 0)
            await PollPackagesAsync().ConfigureAwait(true);

        if (HasListError && !_listBusy)
        {
            _ticksSinceListError++;
            if (_ticksSinceListError >= ListRetryTicks) await RefreshListAsync().ConfigureAwait(true);
        }
    }

    private async Task PollDevicesAsync()
    {
        var devices = await Context.Adb.ListDevicesAsync(CancellationToken.None).ConfigureAwait(true);

        var changed = devices.Count != Devices.Count ||
                      !devices.SequenceEqual(Devices);
        if (!changed) return;

        var previous = _selectedDevice?.Serial ?? Context.Settings.PreferredDeviceSerial;

        Devices.Clear();
        foreach (var device in devices) Devices.Add(device);

        var match = Devices.FirstOrDefault(d => d.Serial == previous) ?? Devices.FirstOrDefault();

        // Assign through the field to avoid re-saving the preferred serial on every re-detect.
        _selectedDevice = match;
        OnPropertyChanged(nameof(SelectedDevice));
        OnPropertyChanged(nameof(DeviceStateText));
        OnPropertyChanged(nameof(DeviceReady));
        OnPropertyChanged(nameof(HasManyDevices));

        if (match?.IsReady == true)
        {
            await PollPackagesAsync().ConfigureAwait(true);
        }
        else
        {
            _installed = [];
            RebuildRows();
        }
    }

    private async Task PollPackagesAsync()
    {
        var device = _selectedDevice;
        if (device is null || !device.IsReady) return;

        var installed = await Context.Adb
            .ListInstalledVersionsAsync(device.Serial, CancellationToken.None)
            .ConfigureAwait(true);

        if (installed.SequenceEqual(_installed)) return;

        _installed = installed;
        RebuildRows();
    }

    // ------------------------------------------------------------------- remote list

    public async Task RefreshListAsync()
    {
        _listBusy = true;
        _ticksSinceListError = 0;
        _refreshCommand.RaiseCanExecuteChanged();

        try
        {
            _remote = await Context.Versions.ListAsync(CancellationToken.None).ConfigureAwait(true);
            ListError = "";
        }
        catch (Exception)
        {
            _remote = null;
            ListError = "Güncelleme sunucusuna ulaşılamadı — yalnız bu bilgisayardaki sürümler gösteriliyor.";
        }
        finally
        {
            _listBusy = false;
            _refreshCommand.RaiseCanExecuteChanged();
            RebuildRows();
        }
    }

    /// <summary>
    /// Re-merges the three sources and syncs <see cref="Rows"/> in place, reusing row view models so
    /// a running download is not interrupted by a refresh.
    /// </summary>
    private void RebuildRows()
    {
        var merged = VersionCatalog.Merge(
            _remote, VersionCatalog.ReadLocal(Context.Paths.GameVersionsDir), _installed);

        var desired = new List<VersionRowViewModel>(merged.Count);
        foreach (var row in merged)
        {
            if (_rowsByVersion.TryGetValue(row.Version, out var existing))
            {
                existing.Update(row);
                desired.Add(existing);
            }
            else
            {
                var created = new VersionRowViewModel(row, this);
                _rowsByVersion[row.Version] = created;
                desired.Add(created);
            }
        }

        for (int i = 0; i < desired.Count; i++)
        {
            if (i >= Rows.Count) Rows.Add(desired[i]);
            else if (!ReferenceEquals(Rows[i], desired[i])) Rows[i] = desired[i];
        }

        while (Rows.Count > desired.Count) Rows.RemoveAt(Rows.Count - 1);
    }

    // -------------------------------------------------------------------- download

    public async Task DownloadAsync(VersionRowViewModel row)
    {
        if (row.Remote is null)
        {
            Fail($"{row.VersionLabel} güncelleme sunucusunda yok; indirilemez.");
            return;
        }

        var target = Path.Combine(Context.Paths.GameVersionsDir, GamePackage.FileNameFor(row.Version));
        row.BeginWork("İndiriliyor…");
        try
        {
            await Context.Versions
                .DownloadAsync(row.Remote, target, new Progress<DownloadProgress>(row.ReportProgress),
                    row.CancellationToken)
                .ConfigureAwait(true);

            row.EndWork("İndirildi");
            Inform($"{row.VersionLabel} indirildi.");
        }
        catch (OperationCanceledException)
        {
            row.EndWork("İptal edildi");
        }
        catch (Exception ex)
        {
            row.EndWork("İndirilemedi");
            Fail($"{row.VersionLabel} indirilemedi: {ex.Message}");
        }
        finally
        {
            RebuildRows();
        }
    }

    // --------------------------------------------------------------------- install

    public async Task InstallAsync(VersionRowViewModel row)
    {
        var device = _selectedDevice;
        if (device is null || !device.IsReady)
        {
            Fail("Yetkili bir gözlük bağlı değil.");
            return;
        }

        if (_installBusy)
        {
            Fail("Bu gözlüğe şu an başka bir kurulum yapılıyor.");
            return;
        }

        if (!row.IsLocal)
        {
            await DownloadAsync(row).ConfigureAwait(true);
            if (!row.IsLocal) return;
        }

        var apk = row.LocalPath;
        if (apk is null || !File.Exists(apk))
        {
            Fail($"{row.VersionLabel} dosyası bulunamadı.");
            return;
        }

        _installBusy = true;
        RefreshDeviceGate();
        row.BeginWork("Kuruluyor…");

        try
        {
            var result = await Context.Adb
                .InstallAsync(device.Serial, apk, row.CancellationToken)
                .ConfigureAwait(true);

            if (result.Combined.Contains("Success", StringComparison.Ordinal))
            {
                row.EndWork("Kuruldu");
                Inform($"{row.VersionLabel} gözlüğe kuruldu.");
                return;
            }

            if (AdbInstallError.IsSignatureConflict(result.Combined))
            {
                await HandleSignatureConflictAsync(device, row, apk).ConfigureAwait(true);
                return;
            }

            var message = result.TimedOut
                ? "Kurulum zaman aşımına uğradı."
                : AdbInstallError.Translate(result.Combined) ?? FirstLine(result.Combined);

            row.EndWork(message);
            Fail($"{row.VersionLabel} kurulamadı: {message}");
        }
        catch (OperationCanceledException)
        {
            row.EndWork("İptal edildi");
        }
        catch (Exception ex)
        {
            row.EndWork("Kurulamadı");
            Fail($"{row.VersionLabel} kurulamadı: {ex.Message}");
        }
        finally
        {
            _installBusy = false;
            RefreshDeviceGate();
            await PollPackagesAsync().ConfigureAwait(true);
        }
    }

    /// <summary>
    /// Same package id signed with another key. The ONLY uninstall the launcher ever performs, and
    /// only after the operator confirms — and only for this single version.
    /// </summary>
    private async Task HandleSignatureConflictAsync(AdbDevice device, VersionRowViewModel row, string apk)
    {
        var confirmed = Dialogs.Confirm(
            $"Gözlükteki {row.VersionLabel} farklı imzayla kurulmuş. Yalnız bu sürüm " +
            $"({row.PackageName}) kaldırılıp yeniden kurulsun mu? Diğer sürümlere dokunulmaz.");

        if (!confirmed)
        {
            row.EndWork("Kurulmadı (imza çakışması)");
            return;
        }

        row.ReportStatus("Eski paket kaldırılıyor…");
        var uninstall = await Context.Adb
            .UninstallAsync(device.Serial, row.PackageName, row.CancellationToken)
            .ConfigureAwait(true);

        if (!uninstall.Combined.Contains("Success", StringComparison.Ordinal))
        {
            row.EndWork("Kaldırılamadı");
            Fail($"{row.PackageName} kaldırılamadı: {FirstLine(uninstall.Combined)}");
            return;
        }

        row.ReportStatus("Kuruluyor…");
        var install = await Context.Adb
            .InstallAsync(device.Serial, apk, row.CancellationToken)
            .ConfigureAwait(true);

        if (install.Combined.Contains("Success", StringComparison.Ordinal))
        {
            row.EndWork("Kuruldu");
            Inform($"{row.VersionLabel} gözlüğe kuruldu.");
            return;
        }

        var message = AdbInstallError.Translate(install.Combined) ?? FirstLine(install.Combined);
        row.EndWork(message);
        Fail($"{row.VersionLabel} kurulamadı: {message}");
    }

    private void RefreshDeviceGate()
    {
        OnPropertyChanged(nameof(DeviceReady));
        foreach (var row in Rows) row.RefreshCommands();
    }

    private void OpenFolder()
    {
        try
        {
            Directory.CreateDirectory(Context.Paths.GameVersionsDir);
            Process.Start(new ProcessStartInfo
            {
                FileName = Context.Paths.GameVersionsDir,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            Fail($"Klasör açılamadı: {ex.Message}");
        }
    }

    private static string FirstLine(string text)
    {
        var line = text.Split('\n').FirstOrDefault(l => l.Trim().Length > 0);
        return line?.Trim() ?? "bilinmeyen hata";
    }
}
