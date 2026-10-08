using System.Windows.Input;
using VortexArena.Launcher.Infrastructure;
using VortexArena.Launcher.Services;

namespace VortexArena.Launcher.ViewModels;

/// <summary>
/// Admin page: starts the desktop admin build pointed at the server.
/// <para>⚠️ Starting is allowed with the server down on purpose — the admin build retries the
/// connection itself and the operator often opens both in the other order.</para>
/// </summary>
public sealed class AdminPageViewModel : PageViewModel
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
    }

    public override string Title => "Yönetim";

    public override string Description =>
        "Maçı yöneten masaüstü uygulaması. Mod ve harita seçimi burada değil, yönetim ekranındadır.";

    public ICommand StartCommand => _startCommand;

    public ICommand StopCommand => _stopCommand;

    public ICommand ApplyAddressCommand { get; }

    public bool IsRunning => Context.Admin.IsRunning;

    public bool CanStart => !IsRunning && !_busy && Context.Paths.AdminExeExists;

    public bool CanStop => IsRunning && !_busy;

    public string RunStateText => IsRunning ? "Çalışıyor" : "Kapalı";

    /// <summary>Empty-state text while <c>admin\</c> has no admin build.</summary>
    public string MissingAdminMessage => Context.Paths.AdminExeExists
        ? ""
        : $"Yönetim oyunu yok. Yönetim build'ini (VortexArena.exe + VortexArena_Data) şu klasöre koyun: {Context.Paths.AdminDir}";

    public string TargetText => $"{Context.Settings.ServerIp.Trim()}:{Context.ControlPort}";

    public string ServerHint => _serverIsUp
        ? "Sunucu çalışıyor."
        : "Sunucu kapalı — yönetim açılır ama bağlanana kadar bekler.";

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
        return Task.CompletedTask;
    }

    private void RefreshRunState()
    {
        OnPropertyChanged(nameof(IsRunning));
        OnPropertyChanged(nameof(RunStateText));
        OnPropertyChanged(nameof(CanStart));
        OnPropertyChanged(nameof(CanStop));
        OnPropertyChanged(nameof(MissingAdminMessage));
        OnPropertyChanged(nameof(TargetText));
        _startCommand.RaiseCanExecuteChanged();
        _stopCommand.RaiseCanExecuteChanged();
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
