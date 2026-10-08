using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows.Input;
using VortexArena.Launcher.Infrastructure;
using VortexArena.Launcher.Services;
using VortexArena.Protocol;

namespace VortexArena.Launcher.ViewModels;

/// <summary>
/// Recording page: the arm switch plus the replay library.
/// <para>⚠️ Recording is server state, not launcher state: the server starts every run with it OFF,
/// so this page only ever mirrors <c>status.recording</c>.</para>
/// </summary>
public sealed class RecordingPageViewModel : PageViewModel
{
    /// <summary>Shell ticks once a second; the file list is cheap but not free.</summary>
    private const int ListRefreshTicks = 5;

    private readonly RelayCommand _toggleCommand;
    private readonly RelayCommand _refreshCommand;

    private LauncherRecordingStatus? _recording;
    private ServerLink _link = ServerLink.Down;
    private string _directory;
    private bool _busy;
    private int _tick;

    public RecordingPageViewModel(LauncherContext context) : base(context)
    {
        _directory = context.Paths.ReplaysDir;
        _toggleCommand = new RelayCommand(() => _ = ToggleAsync(), () => CanToggle);
        _refreshCommand = new RelayCommand(RefreshList);
        OpenFolderCommand = new RelayCommand(OpenFolder);
    }

    public override string Title => "Kayıt";

    public override string Description =>
        "Maç kaydı açıkken her maç kendi dosyasına yazılır; lobide beklerken dosya açılmaz.";

    public ObservableCollection<ReplayRowViewModel> Replays { get; } = [];

    public ICommand ToggleCommand => _toggleCommand;

    public ICommand RefreshCommand => _refreshCommand;

    public ICommand OpenFolderCommand { get; }

    public bool IsArmed => _recording?.armed == true;

    public bool IsActive => _recording?.active == true;

    public bool ServerIsUp => _link == ServerLink.Running;

    public bool CanToggle => ServerIsUp && !_busy;

    public string ToggleText => IsArmed ? "Kaydı durdur" : "Kaydı başlat";

    public string StateText
    {
        get
        {
            if (!ServerIsUp) return "Kayıt için sunucu çalışıyor olmalı";
            if (IsActive)
            {
                var file = Display.OrDash(_recording?.file);
                var elapsed = Display.Clock(TimeSpan.FromMilliseconds(_recording?.elapsedMs ?? 0));
                return $"Kaydediliyor: {file} · {elapsed}";
            }

            return IsArmed ? "Kayıt açık — maç başlayınca kaydedilecek" : "Kayıt kapalı";
        }
    }

    public string DirectoryText => _directory;

    /// <summary>Shell hands over each poll result.</summary>
    public void ApplyStatus(ServerStatusResult result)
    {
        _link = result.Link;
        _recording = result.Status?.recording;

        var directory = _recording?.directory;
        _directory = string.IsNullOrWhiteSpace(directory) ? Context.Paths.ReplaysDir : directory;

        OnPropertyChanged(nameof(IsArmed));
        OnPropertyChanged(nameof(IsActive));
        OnPropertyChanged(nameof(ServerIsUp));
        OnPropertyChanged(nameof(CanToggle));
        OnPropertyChanged(nameof(ToggleText));
        OnPropertyChanged(nameof(StateText));
        OnPropertyChanged(nameof(DirectoryText));
        _toggleCommand.RaiseCanExecuteChanged();

        MarkWritingFile();
    }

    public override Task TickAsync()
    {
        _tick++;
        if (_tick % ListRefreshTicks == 1) RefreshList();
        return Task.CompletedTask;
    }

    private async Task ToggleAsync()
    {
        if (!CanToggle) return;

        var turnOn = !IsArmed;
        _busy = true;
        _toggleCommand.RaiseCanExecuteChanged();

        try
        {
            var result = await Context.StatusClient
                .SetRecordingAsync(Context.ControlPort, turnOn, CancellationToken.None)
                .ConfigureAwait(true);

            if (result.Link != ServerLink.Running)
            {
                Fail(result.Error ?? "Sunucu kayıt isteğine cevap vermedi.");
                return;
            }

            ApplyStatus(result);
            Inform(turnOn ? "Kayıt açıldı." : "Kayıt kapatıldı.");
        }
        finally
        {
            _busy = false;
            _toggleCommand.RaiseCanExecuteChanged();
        }
    }

    private void RefreshList()
    {
        var entries = ReplayLibrary.Read(_directory);

        Replays.Clear();
        foreach (var entry in entries) Replays.Add(new ReplayRowViewModel(entry, this));

        MarkWritingFile();
    }

    private void MarkWritingFile()
    {
        var open = IsActive ? _recording?.file : null;
        foreach (var row in Replays)
        {
            row.IsWriting = !string.IsNullOrEmpty(open) &&
                            string.Equals(row.FileName, open, StringComparison.OrdinalIgnoreCase);
        }
    }

    public void Watch(ReplayRowViewModel row)
    {
        var result = Context.Admin.StartReplay(row.Entry.Path);
        if (!result.Ok)
        {
            Fail(result.Error!);
            return;
        }

        Inform($"{row.FileName} yönetim uygulamasında açılıyor.");
    }

    public void ShowInFolder(ReplayRowViewModel row)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                // /select needs the path quoted as ONE argument, so UseShellExecute stays false.
                Arguments = $"/select,\"{row.Entry.Path}\"",
                UseShellExecute = false,
            });
        }
        catch (Exception ex)
        {
            Fail($"Klasör açılamadı: {ex.Message}");
        }
    }

    private void OpenFolder()
    {
        try
        {
            Directory.CreateDirectory(_directory);
            Process.Start(new ProcessStartInfo
            {
                FileName = _directory,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            Fail($"Klasör açılamadı: {ex.Message}");
        }
    }
}
