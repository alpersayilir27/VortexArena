using System.Windows.Input;
using VortexArena.Launcher.Infrastructure;
using VortexArena.Launcher.Services;

namespace VortexArena.Launcher.ViewModels;

/// <summary>
/// Admin page: starts the desktop admin build pointed at the server, and keeps the admin versions
/// published on the update server side by side under <c>admin_versions\</c>.
/// <para>⚠️ Starting is allowed with the server down on purpose — the admin build retries the
/// connection itself and the operator often opens both in the other order.</para>
/// </summary>
public sealed class AdminPageViewModel : PageViewModel, IBuildVersionListHost
{
    private readonly RelayCommand _startCommand;
    private readonly RelayCommand _stopCommand;

    private bool _serverIsUp;
    private bool _busy;
    private string _ipText;

    public AdminPageViewModel(LauncherContext context) : base(context)
    {
        _ipText = context.Settings.ServerIp;
        _startCommand = new RelayCommand(Start, () => CanStart);
        _stopCommand = new RelayCommand(() => _ = StopAsync(), () => CanStop);
        ApplyAddressCommand = new RelayCommand(ApplyAddress);

        context.Admin.AdoptRunningInstance();
        Versions = new BuildVersionListViewModel(this);
    }

    public override string Title => "Yönetim";

    public override string Description =>
        "Maçı yöneten masaüstü uygulaması. Mod ve harita seçimi burada değil, yönetim ekranındadır.";

    /// <summary>Admin build list card.</summary>
    public BuildVersionListViewModel Versions { get; }

    public ICommand StartCommand => _startCommand;

    public ICommand StopCommand => _stopCommand;

    public ICommand ApplyAddressCommand { get; }

    public bool IsRunning => Context.Admin.IsRunning;

    public bool CanStart => !IsRunning && !_busy && Context.Admin.SelectedVersion > 0;

    public bool CanStop => IsRunning && !_busy;

    public string RunStateText => IsRunning ? "Çalışıyor" : "Kapalı";

    /// <summary>Selected build, or the running one while a build is up.</summary>
    public string VersionText
    {
        get
        {
            var version = IsRunning && Context.Admin.RunningVersion > 0
                ? Context.Admin.RunningVersion
                : Context.Admin.SelectedVersion;

            return version > 0 ? $"Sürüm: v{version}" : "Sürüm: yok";
        }
    }

    /// <summary>Empty-state text while no admin build is unpacked.</summary>
    public string MissingAdminMessage => Context.Admin.SelectedVersion > 0
        ? ""
        : "Yönetim oyunu yok. Aşağıdaki listeden bir sürüm indirin.";

    public string TargetText => $"{Context.Settings.ServerIp.Trim()}:{Context.ControlPort}";

    public string ServerHint => _serverIsUp
        ? "Sunucu çalışıyor."
        : "Sunucu kapalı — yönetim açılır ama bağlanana kadar bekler.";

    /// <summary>A download or extraction is in flight — the exit prompt warns about it.</summary>
    public bool IsWorking => Versions.IsWorking;

    public string IpText
    {
        get => _ipText;
        set => Set(ref _ipText, value);
    }

    /// <summary>Shell tells the page whether the server answered the last poll.</summary>
    public void ApplyServerLink(ServerLink link)
    {
        if (!Set(ref _serverIsUp, link != ServerLink.Down)) return;
        OnPropertyChanged(nameof(ServerHint));
    }

    public override Task TickAsync()
    {
        RefreshRunState();
        Versions.Tick();
        return Task.CompletedTask;
    }

    // ------------------------------------------------------------------ version list

    BuildPackage IBuildVersionListHost.Package => BuildPackage.Admin;

    string IBuildVersionListHost.VersionsDir => Context.Paths.AdminVersionsDir;

    IVersionSource IBuildVersionListHost.Source => Context.AdminVersions;

    int IBuildVersionListHost.PinnedVersion => Context.Settings.AdminVersion;

    int IBuildVersionListHost.SelectedVersion => Context.Admin.SelectedVersion;

    int IBuildVersionListHost.RunningVersion => Context.Admin.RunningVersion;

    bool IBuildVersionListHost.IsRunning => IsRunning;

    IReadOnlyList<int> IBuildVersionListHost.RefreshInstalled()
    {
        Context.Admin.RefreshInstalled();
        return Context.Admin.InstalledVersions;
    }

    void IBuildVersionListHost.PersistSelection(int version)
    {
        Context.Settings.AdminVersion = version;
        Context.SaveSettings();
    }

    void IBuildVersionListHost.OnListChanged() => RefreshRunState();

    void IBuildVersionListHost.ShowInfo(string message) => Inform(message);

    void IBuildVersionListHost.ShowError(string message) => Fail(message);

    string IBuildVersionListHost.RunningBlocksDownload(string versionLabel)
        => $"{versionLabel} şu an çalışıyor; önce yönetim uygulamasını durdurun.";

    string IBuildVersionListHost.SelectionBlocked => "Yönetim çalışırken sürüm değiştirilemez; önce durdurun.";

    string IBuildVersionListHost.SelectionChanged(string versionLabel)
        => $"Yönetim {versionLabel} ile başlatılacak.";

    string IBuildVersionListHost.InstallFinished(string versionLabel)
        => $"{versionLabel} indirildi ve kuruldu.";

    // ----------------------------------------------------------------------- run

    private void RefreshRunState()
    {
        OnPropertyChanged(nameof(IsRunning));
        OnPropertyChanged(nameof(RunStateText));
        OnPropertyChanged(nameof(CanStart));
        OnPropertyChanged(nameof(CanStop));
        OnPropertyChanged(nameof(MissingAdminMessage));
        OnPropertyChanged(nameof(TargetText));
        OnPropertyChanged(nameof(VersionText));
        _startCommand.RaiseCanExecuteChanged();
        _stopCommand.RaiseCanExecuteChanged();
        Versions.RefreshCommands();
    }

    private void Start()
    {
        var result = Context.Admin.Start(Context.ControlPort);
        if (!result.Ok)
        {
            Fail(result.Error!);
            return;
        }

        Inform($"Yönetim başlatıldı → {TargetText}");
        RefreshRunState();
    }

    private async Task StopAsync()
    {
        _busy = true;
        RefreshRunState();
        try
        {
            Inform(await Context.Admin.StopAsync().ConfigureAwait(true));
        }
        finally
        {
            _busy = false;
            RefreshRunState();
        }
    }

    private void ApplyAddress()
    {
        var ip = IpText.Trim();
        if (!LauncherSettings.IsValidIp(ip))
        {
            Fail($"Geçersiz IP: '{ip}'. Örnek: 192.168.1.10");
            return;
        }

        // Not saved: the next launch starts on loopback again (LauncherSettings.ServerIp).
        Context.Settings.ServerIp = ip;
        OnPropertyChanged(nameof(TargetText));
        Inform($"Yönetim bu oturumda {TargetText} adresine bağlanacak; launcher yeniden açılınca {LauncherSettings.DefaultServerIp} olur.");
    }
}
