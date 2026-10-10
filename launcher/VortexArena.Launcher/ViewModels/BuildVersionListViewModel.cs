using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Windows.Input;
using VortexArena.Launcher.Infrastructure;
using VortexArena.Launcher.Services;

namespace VortexArena.Launcher.ViewModels;

/// <summary>
/// What a page gives the shared version list: which package it lists, where it comes from, and how
/// the page's own run state and messages read.
/// </summary>
public interface IBuildVersionListHost
{
    BuildPackage Package { get; }

    /// <summary>Folder holding one subfolder per unpacked build.</summary>
    string VersionsDir { get; }

    IVersionSource Source { get; }

    /// <summary>Operator's saved pick; 0 = automatic.</summary>
    int PinnedVersion { get; }

    /// <summary>Version that would be started now.</summary>
    int SelectedVersion { get; }

    /// <summary>Version currently up; 0 when none (or an unrecognized build) runs.</summary>
    int RunningVersion { get; }

    bool IsRunning { get; }

    /// <summary>Re-reads the unpacked builds; returns them newest first.</summary>
    IReadOnlyList<int> RefreshInstalled();

    /// <summary>Persists the operator's pick — the automatic fallback is never written.</summary>
    void PersistSelection(int version);

    /// <summary>The list changed; the page refreshes whatever depends on the selected build.</summary>
    void OnListChanged();

    void ShowInfo(string message);

    void ShowError(string message);

    /// <summary>Why a download is refused while that version runs.</summary>
    string RunningBlocksDownload(string versionLabel);

    /// <summary>Why the selection cannot change right now.</summary>
    string SelectionBlocked { get; }

    string SelectionChanged(string versionLabel);

    string InstallFinished(string versionLabel);
}

/// <summary>
/// The version list card shared by the Server and Admin pages: remote listing, side-by-side
/// installs and the operator's pick.
/// <para>⚠️ Rows live across refreshes and an in-flight download keeps its own row, so a failed list
/// fetch cannot make a running download's progress and Cancel button disappear.</para>
/// </summary>
public sealed class BuildVersionListViewModel : ObservableObject
{
    /// <summary>Tick spacing (shell ticks once a second).</summary>
    private const int ListRetryTicks = 30;

    private const int InstalledRescanTicks = 5;

    private readonly IBuildVersionListHost _host;
    private readonly RelayCommand _refreshCommand;
    private readonly Dictionary<int, BuildVersionRowViewModel> _rowsByVersion = [];

    /// <summary>Versions whose zip/<c>.extract</c> must survive leftover cleaning.</summary>
    private readonly HashSet<int> _inFlight = [];

    private IReadOnlyList<RemoteVersion>? _remote;

    /// <summary>Unpacked set the rows were last built from. ⚠️ Own copy: the controllers refresh
    /// their cache on Start too, so their "changed" flag can be consumed before this list sees it.</summary>
    private int[] _seenInstalled = [];

    private bool _listBusy;
    private bool _tickBusy;
    private int _tick;
    private int _ticksSinceListError;
    private string _listError = "";

    public BuildVersionListViewModel(IBuildVersionListHost host)
    {
        _host = host;
        _refreshCommand = new RelayCommand(() => _ = RefreshListAsync(), () => !_listBusy);
        OpenFolderCommand = new RelayCommand(OpenFolder);

        RebuildRows();
    }

    public ObservableCollection<BuildVersionRowViewModel> Rows { get; } = [];

    public ICommand RefreshCommand => _refreshCommand;

    public ICommand OpenFolderCommand { get; }

    /// <summary>Read by the rows: the selection cannot change while the build runs.</summary>
    public bool IsRunning => _host.IsRunning;

    /// <summary>A download or extraction is in flight — the exit prompt warns about it.</summary>
    public bool IsWorking => _inFlight.Count > 0 || Rows.Any(r => r.IsBusy);

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
    /// ⚠️ Does NOT block the shell tick: fetching the list can take seconds and the server status
    /// poll must keep running. The next tick is skipped while it is in flight.
    /// </summary>
    public void Tick()
    {
        if (_tickBusy) return;

        _tickBusy = true;
        _ = RunTickAsync();
    }

    private async Task RunTickAsync()
    {
        try
        {
            _tick++;

            if (_tick == 1)
            {
                await RefreshListAsync().ConfigureAwait(true);
                return;
            }

            if (HasListError && !_listBusy)
            {
                _ticksSinceListError++;
                if (_ticksSinceListError >= ListRetryTicks) await RefreshListAsync().ConfigureAwait(true);
            }

            // A build copied in by hand (or a deleted folder) must show without a manual refresh.
            if (_tick % InstalledRescanTicks == 0 && !_listBusy && RescanInstalled())
            {
                RebuildRows();
                _host.OnListChanged();
            }
        }
        catch (Exception ex)
        {
            _host.ShowError($"Sürüm listesi alınamadı: {ex.Message}");
        }
        finally
        {
            _tickBusy = false;
        }
    }

    // ------------------------------------------------------------------- remote list

    public async Task RefreshListAsync()
    {
        _listBusy = true;
        _ticksSinceListError = 0;
        _refreshCommand.RaiseCanExecuteChanged();

        try
        {
            _remote = await _host.Source.ListAsync(CancellationToken.None).ConfigureAwait(true);
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

            // Half-finished installs are swept only when nothing is writing into the folder.
            if (_inFlight.Count == 0)
                BuildVersionCatalog.CleanLeftovers(_host.VersionsDir, _host.Package, _inFlight);

            RescanInstalled();
            RebuildRows();
            _host.OnListChanged();
        }
    }

    /// <summary>Re-reads the unpacked builds; true when the set differs from the one last shown.</summary>
    private bool RescanInstalled()
    {
        var installed = _host.RefreshInstalled();
        if (installed.SequenceEqual(_seenInstalled)) return false;

        _seenInstalled = installed.ToArray();
        return true;
    }

    /// <summary>
    /// Re-merges remote and unpacked builds and syncs <see cref="Rows"/> in place, reusing row view
    /// models so a running download is not interrupted by a refresh.
    /// </summary>
    private void RebuildRows()
    {
        var merged = BuildVersionCatalog.Merge(
            _remote, BuildVersionCatalog.ReadInstalled(_host.VersionsDir, _host.Package));
        var selected = _host.SelectedVersion;
        var pinned = _host.PinnedVersion;

        var desired = new List<BuildVersionRowViewModel>(merged.Count);
        foreach (var row in merged)
        {
            if (_rowsByVersion.TryGetValue(row.Version, out var existing))
            {
                existing.Update(row, selected, pinned);
                desired.Add(existing);
            }
            else
            {
                var created = new BuildVersionRowViewModel(row, this);
                created.Update(row, selected, pinned);
                _rowsByVersion[row.Version] = created;
                desired.Add(created);
            }
        }

        // ⚠️ A failed list fetch drops remote-only versions; a row still downloading must stay, or
        // its progress and Cancel button vanish while the download runs on.
        foreach (var busy in _rowsByVersion.Values)
        {
            if ((busy.IsBusy || _inFlight.Contains(busy.Version)) && !desired.Contains(busy)) desired.Add(busy);
        }

        desired.Sort((a, b) => b.Version.CompareTo(a.Version));

        for (int i = 0; i < desired.Count; i++)
        {
            if (i >= Rows.Count) Rows.Add(desired[i]);
            else if (!ReferenceEquals(Rows[i], desired[i])) Rows[i] = desired[i];
        }

        while (Rows.Count > desired.Count) Rows.RemoveAt(Rows.Count - 1);
    }

    /// <summary>Re-evaluates every row's buttons; called when the page's run state changes.</summary>
    public void RefreshCommands()
    {
        OnPropertyChanged(nameof(IsRunning));
        foreach (var row in Rows) row.RefreshCommands();
    }

    // ------------------------------------------------------------- download + unzip

    /// <summary>
    /// Fetches <c>&lt;prefix&gt;&lt;N&gt;.zip</c> and unpacks it into its own folder.
    /// <para>⚠️ Only this version's files are ever touched; another version's folder stays as it is.</para>
    /// </summary>
    public async Task DownloadAsync(BuildVersionRowViewModel row)
    {
        if (row.Remote is null)
        {
            _host.ShowError($"{row.VersionLabel} güncelleme sunucusunda yok; indirilemez.");
            return;
        }

        if (_host.IsRunning && _host.RunningVersion == row.Version)
        {
            _host.ShowError(_host.RunningBlocksDownload(row.VersionLabel));
            return;
        }

        var package = _host.Package;
        var versionsDir = _host.VersionsDir;
        var zip = Path.Combine(versionsDir, package.FileNameFor(row.Version));
        var staging = Path.Combine(versionsDir, package.ExtractFolderNameFor(row.Version));
        var target = Path.Combine(versionsDir, package.FolderNameFor(row.Version));

        _inFlight.Add(row.Version);
        row.BeginWork("İndiriliyor…");
        try
        {
            await _host.Source
                .DownloadAsync(row.Remote, zip, new Progress<DownloadProgress>(row.ReportProgress),
                    row.CancellationToken)
                .ConfigureAwait(true);

            row.BeginExtract("Açılıyor…");
            await Task.Run(() => Install(zip, staging, target, package.ExeName)).ConfigureAwait(true);

            row.EndWork("Kuruldu");
            _host.ShowInfo(_host.InstallFinished(row.VersionLabel));
        }
        catch (OperationCanceledException) when (row.CancellationToken.IsCancellationRequested)
        {
            // Only the operator's cancel; an HttpClient timeout falls through as an error.
            Discard(zip, staging);
            row.EndWork("İptal edildi");
        }
        catch (Exception ex)
        {
            Discard(zip, staging);
            row.EndWork("Kurulamadı");
            _host.ShowError($"{row.VersionLabel} kurulamadı: {ex.Message}");
        }
        finally
        {
            _inFlight.Remove(row.Version);
            RescanInstalled();
            RebuildRows();
            _host.OnListChanged();
        }
    }

    /// <summary>
    /// Unzips beside the final folder and renames only when the exe is really there.
    /// <para>⚠️ A half-unpacked version folder would count as installed and the operator would start
    /// a broken build.</para>
    /// </summary>
    private static void Install(string zipPath, string stagingDir, string targetDir, string exeName)
    {
        if (Directory.Exists(stagingDir)) Directory.Delete(stagingDir, recursive: true);
        ZipFile.ExtractToDirectory(zipPath, stagingDir);

        if (!File.Exists(Path.Combine(stagingDir, exeName)))
            throw new IOException($"Arşivde {exeName} yok.");

        if (Directory.Exists(targetDir)) Directory.Delete(targetDir, recursive: true);
        MoveWithRetry(stagingDir, targetDir);

        try
        {
            File.Delete(zipPath);
        }
        catch (Exception)
        {
            // Installed already; a locked zip is swept on the next list refresh.
        }
    }

    /// <summary>Antivirus often holds freshly unzipped files open for a moment ("access denied").</summary>
    private static void MoveWithRetry(string from, string to)
    {
        const int attempts = 4;
        for (int i = 1; ; i++)
        {
            try
            {
                Directory.Move(from, to);
                return;
            }
            catch (Exception ex) when (i < attempts && ex is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(500 * i);
            }
        }
    }

    private static void Discard(string zipPath, string stagingDir)
    {
        try
        {
            if (Directory.Exists(stagingDir)) Directory.Delete(stagingDir, recursive: true);
        }
        catch (Exception)
        {
            // Swept on the next list refresh.
        }

        try
        {
            if (File.Exists(zipPath)) File.Delete(zipPath);
        }
        catch (Exception)
        {
            // Swept on the next list refresh.
        }
    }

    // -------------------------------------------------------------------- selection

    /// <summary>Persists the operator's pick; the automatic fallback is never written.</summary>
    public void SelectVersion(BuildVersionRowViewModel row)
    {
        if (_host.IsRunning)
        {
            _host.ShowError(_host.SelectionBlocked);
            return;
        }

        if (!File.Exists(Path.Combine(_host.VersionsDir, _host.Package.FolderNameFor(row.Version),
                _host.Package.ExeName)))
        {
            _host.ShowError($"{row.VersionLabel} bu bilgisayarda yok.");
            return;
        }

        _host.PersistSelection(row.Version);
        RescanInstalled();
        RebuildRows();
        _host.OnListChanged();
        _host.ShowInfo(_host.SelectionChanged(row.VersionLabel));
    }

    private void OpenFolder()
    {
        try
        {
            Directory.CreateDirectory(_host.VersionsDir);
            Process.Start(new ProcessStartInfo
            {
                FileName = _host.VersionsDir,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            _host.ShowError($"Klasör açılamadı: {ex.Message}");
        }
    }
}
