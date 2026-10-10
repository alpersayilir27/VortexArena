using System.Globalization;
using System.Threading;
using System.Windows.Input;
using VortexArena.Launcher.Infrastructure;
using VortexArena.Launcher.Services;

namespace VortexArena.Launcher.ViewModels;

/// <summary>
/// One build row (server or admin). Lives across refreshes (<see cref="Update"/>) so an in-flight
/// download keeps its progress instead of being replaced by a fresh object.
/// </summary>
public sealed class BuildVersionRowViewModel : ObservableObject
{
    private readonly BuildVersionListViewModel _list;
    private readonly RelayCommand _downloadCommand;
    private readonly RelayCommand _selectCommand;
    private readonly RelayCommand _cancelCommand;

    private BuildVersionRow _row;
    private CancellationTokenSource? _cancellation;
    private bool _busy;

    /// <summary>Extraction cannot be interrupted, so the cancel button disappears for it.</summary>
    private bool _cancellable;

    private bool _selected;

    /// <summary>The operator's saved pick (settings), as opposed to the automatic fallback.</summary>
    private bool _pinned;

    private double _progress;
    private string _status = "";

    public BuildVersionRowViewModel(BuildVersionRow row, BuildVersionListViewModel list)
    {
        _row = row;
        _list = list;
        _downloadCommand = new RelayCommand(() => _ = _list.DownloadAsync(this), () => CanDownload);
        _selectCommand = new RelayCommand(() => _list.SelectVersion(this), () => CanSelect);
        _cancelCommand = new RelayCommand(Cancel, () => CanCancel);
    }

    public int Version => _row.Version;

    public string VersionLabel => "v" + Version.ToString(CultureInfo.InvariantCulture);

    public string DateText => Display.OrDash(_row.Modified);

    public string SizeText => Display.Megabytes(_row.Size);

    public bool IsInstalled => _row.Installed;

    public bool CloudWarning => _row.CloudWarning;

    public string CloudTooltip =>
        "Bulutla eşitlenmemiş: bu sürüm güncelleme sunucusunda yok (ya da sunucuya ulaşılamadı).";

    public RemoteVersion? Remote => _row.Remote;

    /// <summary>This is the build the launcher starts.</summary>
    public bool IsSelected => _selected;

    public string SelectedText => _pinned ? "Seçili" : "Seçili (otomatik)";

    public ICommand DownloadCommand => _downloadCommand;

    public ICommand SelectCommand => _selectCommand;

    public ICommand CancelCommand => _cancelCommand;

    public bool CanDownload => !_busy && _row.CanDownload;

    /// <summary>Also offered on the automatic pick: otherwise a newer download would silently take over.</summary>
    public bool CanSelect => !_busy && _row.Installed && !_pinned && !_list.IsRunning;

    public bool IsBusy => _busy;

    public bool CanCancel => _busy && _cancellable;

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

    public void Update(BuildVersionRow row, int selectedVersion, int pinnedVersion)
    {
        _row = row;
        _selected = row.Installed && row.Version == selectedVersion;
        _pinned = row.Installed && row.Version == pinnedVersion;
        OnPropertyChanged(nameof(DateText));
        OnPropertyChanged(nameof(SizeText));
        OnPropertyChanged(nameof(IsInstalled));
        OnPropertyChanged(nameof(CloudWarning));
        OnPropertyChanged(nameof(IsSelected));
        OnPropertyChanged(nameof(SelectedText));
        RefreshCommands();
    }

    public void BeginWork(string status)
    {
        _cancellation = new CancellationTokenSource();
        _busy = true;
        _cancellable = true;
        Progress = 0;
        StatusText = status;
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(ShowProgress));
        RefreshCommands();
    }

    /// <summary>Second phase: unzip runs to the end even if the operator clicks away.</summary>
    public void BeginExtract(string status)
    {
        _cancellable = false;
        Progress = 100;
        StatusText = status;
        RefreshCommands();
    }

    public void EndWork(string status)
    {
        _cancellation?.Dispose();
        _cancellation = null;
        _busy = false;
        _cancellable = false;
        StatusText = status;
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(ShowProgress));
        RefreshCommands();
    }

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
        OnPropertyChanged(nameof(CanSelect));
        OnPropertyChanged(nameof(CanCancel));
        _downloadCommand.RaiseCanExecuteChanged();
        _selectCommand.RaiseCanExecuteChanged();
        _cancelCommand.RaiseCanExecuteChanged();
    }
}
