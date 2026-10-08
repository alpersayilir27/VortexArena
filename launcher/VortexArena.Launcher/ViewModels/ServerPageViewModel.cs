using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Windows.Input;
using VortexArena.Launcher.Infrastructure;
using VortexArena.Launcher.Services;
using VortexArena.Protocol;

namespace VortexArena.Launcher.ViewModels;

/// <summary>Server lifecycle as the page shows it.</summary>
public enum ServerRunState
{
    Stopped,
    Starting,
    Running,
    Stopping,

    /// <summary>Answering on the port but without the launcher endpoints.</summary>
    Incompatible,
}

/// <summary>
/// Server page: venue choice, start/stop, live status and the log tail.
/// <para>⚠️ While a start or stop is in flight the shell's polling result is IGNORED
/// (<see cref="_transition"/>): the transition loop owns the state until it finishes, otherwise a
/// 700 ms poll would flip "Başlıyor" back to "Kapalı" every second.</para>
/// </summary>
public sealed class ServerPageViewModel : PageViewModel
{
    /// <summary>How long a start is given to answer on the control port.</summary>
    private static readonly TimeSpan StartupBudget = TimeSpan.FromSeconds(20);

    /// <summary>How long a clean shutdown is given before the process is killed.</summary>
    private static readonly TimeSpan ShutdownBudget = TimeSpan.FromSeconds(10);

    private const int MaxLogLines = 400;

    private readonly Queue<string> _logLines = new();
    private readonly LogTailer _tailer;
    private readonly RelayCommand _startCommand;
    private readonly RelayCommand _stopCommand;

    private bool _transition;
    private bool _serverPresent;
    private bool _mapsPresent;
    private ServerRunState _runState = ServerRunState.Stopped;
    private VenueInfo? _selectedVenue;
    private VenueCatalog _catalog = VenueCatalog.Empty;
    private string _logText = "";
    private string _portText = "";
    private string _venue = "—";
    private string _phase = "—";
    private string _mode = "—";
    private string _scene = "—";
    private string _players = "—";
    private string _admins = "—";
    private string _uptime = "—";
    private int _protocolVersion;

    public ServerPageViewModel(LauncherContext context) : base(context)
    {
        _tailer = new LogTailer(context.Paths.ServerLogsDir);
        _startCommand = new RelayCommand(() => _ = StartAsync(), () => CanStart);
        _stopCommand = new RelayCommand(() => _ = StopAsync(), () => CanStop);

        OpenLogsFolderCommand = new RelayCommand(OpenLogsFolder);
        ApplyPortCommand = new RelayCommand(ApplyPort);

        _portText = context.Settings.ControlPortOverride > 0
            ? context.Settings.ControlPortOverride.ToString(CultureInfo.InvariantCulture)
            : "";

        _serverPresent = context.Paths.ServerExeExists;
        _mapsPresent = context.Paths.ServerMapsJsonExists;
        ReloadVenues();
    }

    public override string Title => "Sunucu";

    public override string Description =>
        "Maç sunucusu bu bilgisayarda çalışır. Mekanı seçip başlatın; gözlükler lobiye düşer.";

    public ObservableCollection<VenueInfo> Venues { get; } = [];

    public ICommand StartCommand => _startCommand;

    public ICommand StopCommand => _stopCommand;

    public ICommand OpenLogsFolderCommand { get; }

    public ICommand ApplyPortCommand { get; }

    // ------------------------------------------------------------------ page state

    public ServerRunState RunState
    {
        get => _runState;
        private set
        {
            if (!Set(ref _runState, value)) return;
            OnPropertyChanged(nameof(RunStateText));
            OnPropertyChanged(nameof(IsRunning));
            OnPropertyChanged(nameof(IsBusy));
            OnPropertyChanged(nameof(CanStart));
            OnPropertyChanged(nameof(CanStop));
            OnPropertyChanged(nameof(VenuePickerEnabled));
            _startCommand.RaiseCanExecuteChanged();
            _stopCommand.RaiseCanExecuteChanged();
        }
    }

    public string RunStateText => RunState switch
    {
        ServerRunState.Starting => "Başlıyor",
        ServerRunState.Running => "Çalışıyor",
        ServerRunState.Stopping => "Kapanıyor",
        ServerRunState.Incompatible => "Uyumsuz (eski sürüm)",
        _ => "Kapalı",
    };

    public bool IsRunning => RunState is ServerRunState.Running or ServerRunState.Incompatible;

    public bool IsBusy => RunState is ServerRunState.Starting or ServerRunState.Stopping;

    public bool CanStart => RunState == ServerRunState.Stopped && Context.Paths.ServerExeExists;

    public bool CanStop => RunState is ServerRunState.Running or ServerRunState.Incompatible;

    public bool VenuePickerEnabled => RunState == ServerRunState.Stopped;

    /// <summary>Empty-state text while <c>server\</c> has no server build; empty when fine.</summary>
    public string MissingServerMessage => Context.Paths.ServerExeExists
        ? ""
        : $"Sunucu yok. Sunucu build'ini (VortexArena.Server.App.exe + config) şu klasöre koyun: {Context.Paths.ServerDir}";

    public string CatalogProblem => _catalog.Problem ?? "";

    public string VenueText { get => _venue; private set => Set(ref _venue, value); }

    public string PhaseText { get => _phase; private set => Set(ref _phase, value); }

    public string ModeText { get => _mode; private set => Set(ref _mode, value); }

    public string SceneText { get => _scene; private set => Set(ref _scene, value); }

    public string PlayersText { get => _players; private set => Set(ref _players, value); }

    public string AdminsText { get => _admins; private set => Set(ref _admins, value); }

    public string UptimeText { get => _uptime; private set => Set(ref _uptime, value); }

    public int ProtocolVersion
    {
        get => _protocolVersion;
        private set => Set(ref _protocolVersion, value);
    }

    public string PortSummary =>
        $"Kullanılan kontrol portu: {Context.ControlPort} (boş bırakılırsa server.json'dan okunur).";

    public string PortText
    {
        get => _portText;
        set => Set(ref _portText, value);
    }

    public string LogText
    {
        get => _logText;
        private set => Set(ref _logText, value);
    }

    public VenueInfo? SelectedVenue
    {
        get => _selectedVenue;
        set
        {
            if (!Set(ref _selectedVenue, value)) return;
            if (value is null) return;

            Context.Settings.Venue = value.Name;
            Context.SaveSettings();
        }
    }

    // ---------------------------------------------------------------------- venues

    public void ReloadVenues()
    {
        _catalog = VenueCatalog.ForServerExe(Context.Paths.ServerExe);

        Venues.Clear();
        foreach (var venue in _catalog.Venues) Venues.Add(venue);

        var saved = Context.Settings.Venue;
        _selectedVenue = Venues.FirstOrDefault(v =>
                             string.Equals(v.Name, saved, StringComparison.OrdinalIgnoreCase))
                         ?? Venues.FirstOrDefault();

        OnPropertyChanged(nameof(SelectedVenue));
        OnPropertyChanged(nameof(CatalogProblem));
        OnPropertyChanged(nameof(MissingServerMessage));
    }

    // ----------------------------------------------------------------- shell input

    /// <summary>Applies a poll result from the shell. No-op during a transition.</summary>
    public void ApplyStatus(ServerStatusResult result)
    {
        if (_transition) return;

        RunState = result.Link switch
        {
            ServerLink.Running => ServerRunState.Running,
            ServerLink.Incompatible => ServerRunState.Incompatible,
            _ => ServerRunState.Stopped,
        };

        ShowStatusFields(result.Status);
    }

    public override Task TickAsync()
    {
        DetectServerBuild();
        PumpLog();
        UptimeText = Context.Server.Uptime is { } uptime ? Display.Clock(uptime) : "—";
        return Task.CompletedTask;
    }

    /// <summary>A build copied into (or removed from) <c>server\</c> applies without a restart.</summary>
    private void DetectServerBuild()
    {
        var present = Context.Paths.ServerExeExists;
        // Watched too: a copy in progress can land the exe before config\.
        var maps = Context.Paths.ServerMapsJsonExists;
        if (present == _serverPresent && maps == _mapsPresent) return;

        _serverPresent = present;
        _mapsPresent = maps;
        Context.RefreshControlPort();
        ReloadVenues();
        OnPropertyChanged(nameof(CanStart));
        _startCommand.RaiseCanExecuteChanged();
    }

    private void ShowStatusFields(LauncherStatus? status)
    {
        if (status is null)
        {
            VenueText = PhaseText = ModeText = SceneText = PlayersText = AdminsText = "—";
            ProtocolVersion = 0;
            return;
        }

        VenueText = Display.OrDash(status.venue);
        PhaseText = Display.Phase(status.phase);
        ModeText = Display.OrDash(status.modeId);
        SceneText = Display.OrDash(status.sceneName);
        PlayersText = status.playerCount.ToString(CultureInfo.CurrentCulture);
        AdminsText = status.adminCount.ToString(CultureInfo.CurrentCulture);
        ProtocolVersion = status.protocolVersion;
    }

    // ------------------------------------------------------------------ start/stop

    private async Task StartAsync()
    {
        if (!CanStart) return;

        var venue = SelectedVenue?.Name ?? Context.Settings.Venue;
        var problem = LauncherSettings.ValidateVenue(venue, _catalog.Names);
        if (problem != null)
        {
            Fail(problem);
            return;
        }

        _transition = true;
        RunState = ServerRunState.Starting;
        Inform($"Sunucu başlatılıyor: {venue}");

        try
        {
            var start = Context.Server.Start(venue);
            if (!start.Ok)
            {
                Fail(start.Error!);
                RunState = ServerRunState.Stopped;
                return;
            }

            var deadline = DateTime.UtcNow + StartupBudget;
            while (DateTime.UtcNow < deadline)
            {
                await Task.Delay(500).ConfigureAwait(true);

                if (start.Process is { HasExited: true } exited)
                {
                    Fail(Context.Server.DescribeEarlyExit(exited));
                    Context.Server.ForgetStarted();
                    RunState = ServerRunState.Stopped;
                    return;
                }

                var result = await Context.StatusClient
                    .GetStatusAsync(Context.ControlPort, CancellationToken.None)
                    .ConfigureAwait(true);

                if (result.Link == ServerLink.Running)
                {
                    RunState = ServerRunState.Running;
                    ShowStatusFields(result.Status);
                    Inform($"Sunucu çalışıyor: {venue}");
                    return;
                }

                if (result.Link == ServerLink.Incompatible)
                {
                    RunState = ServerRunState.Incompatible;
                    Fail(ServerStatusClient.IncompatibleMessage);
                    return;
                }
            }

            RunState = ServerRunState.Stopped;
            Fail($"Sunucu {StartupBudget.TotalSeconds:N0} saniyede yanıt vermedi. Aşağıdaki günlüğe bakın.");
        }
        finally
        {
            _transition = false;
        }
    }

    private async Task StopAsync()
    {
        if (!CanStop) return;

        _transition = true;
        var wasIncompatible = RunState == ServerRunState.Incompatible;
        RunState = ServerRunState.Stopping;
        Inform("Sunucu kapatılıyor…");

        try
        {
            if (!wasIncompatible)
            {
                var error = await Context.StatusClient
                    .RequestShutdownAsync(Context.ControlPort, CancellationToken.None)
                    .ConfigureAwait(true);

                if (error == null && await WaitForShutdownAsync().ConfigureAwait(true))
                {
                    Context.Server.ForgetStarted();
                    RunState = ServerRunState.Stopped;
                    ShowStatusFields(null);
                    Inform("Sunucu temiz kapandı.");
                    return;
                }
            }

            var killed = Context.Server.KillProcesses();
            RunState = ServerRunState.Stopped;
            ShowStatusFields(null);

            if (killed > 0)
                Fail("Sunucu zorla kapatıldı — açık kayıt dosyası yarım kalmış olabilir.");
            else
                Inform("Sunucu süreci bulunamadı; kapalı sayılıyor.");
        }
        finally
        {
            _transition = false;
        }
    }

    /// <summary>The Stop button's path, for the launcher's "close everything" exit.</summary>
    public Task StopForExitAsync() => StopAsync();

    /// <summary>True when the port went quiet and the tracked process (if any) exited.</summary>
    private async Task<bool> WaitForShutdownAsync()
    {
        var deadline = DateTime.UtcNow + ShutdownBudget;
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(500).ConfigureAwait(true);

            var result = await Context.StatusClient
                .GetStatusAsync(Context.ControlPort, CancellationToken.None)
                .ConfigureAwait(true);

            if (result.Link != ServerLink.Down) continue;

            var started = Context.Server.Started;
            if (started is null) return true;

            try
            {
                if (started.HasExited) return true;
            }
            catch (Exception)
            {
                return true;
            }
        }

        return false;
    }

    // ------------------------------------------------------------------------- log

    private void PumpLog()
    {
        var lines = _tailer.ReadNew();
        if (_tailer.FileChanged) _logLines.Clear();
        if (lines.Count == 0 && !_tailer.FileChanged) return;

        foreach (var line in lines)
        {
            _logLines.Enqueue(line);
            while (_logLines.Count > MaxLogLines) _logLines.Dequeue();
        }

        LogText = string.Join(Environment.NewLine, _logLines);
    }

    private void OpenLogsFolder()
    {
        try
        {
            Directory.CreateDirectory(Context.Paths.ServerLogsDir);
            Process.Start(new ProcessStartInfo
            {
                FileName = Context.Paths.ServerLogsDir,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            Fail($"Günlük klasörü açılamadı: {ex.Message}");
        }
    }

    private void ApplyPort()
    {
        var text = PortText.Trim();
        if (text.Length == 0)
        {
            Context.Settings.ControlPortOverride = 0;
        }
        else if (int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var port) &&
                 LauncherSettings.IsValidPort(port))
        {
            Context.Settings.ControlPortOverride = port;
        }
        else
        {
            Fail($"Geçersiz port: '{text}'. 1-65535 arası bir sayı yazın ya da boş bırakın.");
            return;
        }

        Context.SaveSettings();
        Context.RefreshControlPort();
        OnPropertyChanged(nameof(PortSummary));
        Inform($"Kontrol portu: {Context.ControlPort}");
    }
}
