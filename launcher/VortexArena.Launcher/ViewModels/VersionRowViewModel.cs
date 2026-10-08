using System.Globalization;
using System.Threading;
using System.Windows.Input;
using VortexArena.Launcher.Infrastructure;
using VortexArena.Launcher.Services;

namespace VortexArena.Launcher.ViewModels;

/// <summary>
/// One version row. Lives across refreshes (<see cref="Update"/>) so an in-flight download keeps
/// its progress instead of being replaced by a fresh object.
/// </summary>
public sealed class VersionRowViewModel : ObservableObject
{
    private readonly VersionsPageViewModel _page;
    private readonly RelayCommand _downloadCommand;
    private readonly RelayCommand _installCommand;
    private readonly RelayCommand _cancelCommand;

    private VersionRow _row;
    private CancellationTokenSource? _cancellation;
    private bool _busy;
    private double _progress;
    private string _status = "";

    public VersionRowViewModel(VersionRow row, VersionsPageViewModel page)
    {
        _row = row;
        _page = page;
        _downloadCommand = new RelayCommand(() => _ = _page.DownloadAsync(this), () => CanDownload);
        _installCommand = new RelayCommand(() => _ = _page.InstallAsync(this), () => CanInstall);
        _cancelCommand = new RelayCommand(Cancel, () => _busy);
    }

    public int Version => _row.Version;

    public string VersionLabel => "v" + Version.ToString(CultureInfo.InvariantCulture);

    public string PackageName => GamePackage.NameFor(Version);

    public string DateText => Display.OrDash(_row.Modified);

    public string SizeText => Display.Megabytes(_row.Size);

    public bool IsLocal => _row.Local;

    public bool IsInstalled => _row.Installed;

    public bool CloudWarning => _row.CloudWarning;

    public string CloudTooltip =>
        "Bulutla eşitlenmemiş: bu sürüm güncelleme sunucusunda yok (ya da sunucuya ulaşılamadı).";

    public string? LocalPath => _row.LocalPath;

    public RemoteVersion? Remote => _row.Remote;

    public ICommand DownloadCommand => _downloadCommand;

    public ICommand InstallCommand => _installCommand;

    public ICommand CancelCommand => _cancelCommand;

    public bool CanDownload => !_busy && _row.CanDownload;

    public bool CanInstall => !_busy && _row.CanInstall && _page.DeviceReady;

    public bool IsBusy => _busy;

    /// <summary>0-100; the bar is hidden when the row is idle.</summary>
    public double Progress
    {
        get => _progress;
        private set => Set(ref _progress, value);
    }

    public bool ShowProgress => _busy;

    public string StatusText
    {
        get => _status;
        private set => Set(ref _status, value);
    }

    /// <summary>Token for the row's current operation; null when idle.</summary>
    public CancellationToken CancellationToken => _cancellation?.Token ?? CancellationToken.None;

    public void Update(VersionRow row)
    {
        _row = row;
        OnPropertyChanged(nameof(DateText));
        OnPropertyChanged(nameof(SizeText));
        OnPropertyChanged(nameof(IsLocal));
        OnPropertyChanged(nameof(IsInstalled));
        OnPropertyChanged(nameof(CloudWarning));
        RefreshCommands();
    }

    public void BeginWork(string status)
    {
        _cancellation = new CancellationTokenSource();
        _busy = true;
        Progress = 0;
        StatusText = status;
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(ShowProgress));
        RefreshCommands();
    }

    public void EndWork(string status)
    {
        _cancellation?.Dispose();
        _cancellation = null;
        _busy = false;
        StatusText = status;
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(ShowProgress));
        RefreshCommands();
    }

    public void ReportStatus(string status) => StatusText = status;

    public void ReportProgress(DownloadProgress value)
    {
        Progress = value.Total > 0 ? value.Received * 100.0 / value.Total : 0;
        StatusText = value.Total > 0
            ? $"İndiriliyor… {Display.Megabytes(value.Received)} / {Display.Megabytes(value.Total)}"
            : $"İndiriliyor… {Display.Megabytes(value.Received)}";
    }

    public void Cancel()
    {
        try
        {
            _cancellation?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Work already finished.
        }
    }

    public void RefreshCommands()
    {
        OnPropertyChanged(nameof(CanDownload));
        OnPropertyChanged(nameof(CanInstall));
        _downloadCommand.RaiseCanExecuteChanged();
        _installCommand.RaiseCanExecuteChanged();
        _cancelCommand.RaiseCanExecuteChanged();
    }
}
