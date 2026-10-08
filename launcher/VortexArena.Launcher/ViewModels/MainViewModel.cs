using System.Collections.ObjectModel;
using System.Reflection;
using System.Threading;
using System.Windows.Input;
using System.Windows.Threading;
using VortexArena.Launcher.Infrastructure;
using VortexArena.Launcher.Services;

namespace VortexArena.Launcher.ViewModels;

/// <summary>
/// Shell: sidebar, page switching and the single polling clock.
/// <para>⚠️ <b>One timer for everything.</b> Pages count ticks instead of owning timers, so the
/// sidebar badges (server, admin, recording, headset) stay live on every page.</para>
/// </summary>
public sealed class MainViewModel : ObservableObject, IDisposable
{
    private readonly LauncherContext _context;
    private readonly DispatcherTimer _timer;
    private readonly PageViewModel[] _pages;

    private bool _ticking;
    private NavItemViewModel? _current;

    public MainViewModel(IReadOnlyList<string> args)
    {
        var paths = LauncherPaths.Resolve(args);
        paths.EnsureLayout();
        _context = new LauncherContext(paths, LauncherSettings.Load());

        ServerPage = new ServerPageViewModel(_context);
        AdminPage = new AdminPageViewModel(_context);
        VersionsPage = new VersionsPageViewModel(_context);
        RecordingPage = new RecordingPageViewModel(_context);
        _pages = [ServerPage, AdminPage, VersionsPage, RecordingPage];

        NavItems =
        [
            new NavItemViewModel("Sunucu", "IconServer", ServerPage),
            new NavItemViewModel("Yönetim", "IconAdmin", AdminPage),
            new NavItemViewModel("Versiyonlar", "IconVersions", VersionsPage),
            new NavItemViewModel("Kayıt", "IconRecording", RecordingPage),
        ];

        NavigateCommand = new RelayCommand(target =>
        {
            if (target is NavItemViewModel item) Current = item;
        });

        Current = NavItems[0];

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => _ = TickAsync();
    }

    public ObservableCollection<NavItemViewModel> NavItems { get; }

    public ICommand NavigateCommand { get; }

    public ServerPageViewModel ServerPage { get; }

    public AdminPageViewModel AdminPage { get; }

    public VersionsPageViewModel VersionsPage { get; }

    public RecordingPageViewModel RecordingPage { get; }

    public NavItemViewModel? Current
    {
        get => _current;
        private set
        {
            if (_current is not null) _current.IsSelected = false;
            if (!Set(ref _current, value)) return;
            if (_current is not null) _current.IsSelected = true;
            OnPropertyChanged(nameof(CurrentPage));
        }
    }

    public PageViewModel? CurrentPage => _current?.Page;

    public string ProductName => "VortexArena";

    public string VersionText => "v" + (Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0");

    public string RootText => LauncherPaths.Shorten(_context.Paths.Root);

    public string RootTooltip => $"{_context.Paths.Root}\nKök: {_context.Paths.Source}";

    /// <summary>Something would be interrupted by closing the launcher.</summary>
    public bool HasBackgroundWork => VersionsPage.IsWorking;

    public bool ServerIsRunning => ServerPage.IsRunning;

    public bool AdminIsRunning => AdminPage.IsRunning;

    public void Start()
    {
        _timer.Start();
        _ = TickAsync();
    }

    private async Task TickAsync()
    {
        if (_ticking) return;
        _ticking = true;
        try
        {
            var status = await _context.StatusClient
                .GetStatusAsync(_context.ControlPort, CancellationToken.None)
                .ConfigureAwait(true);

            ServerPage.ApplyStatus(status);
            RecordingPage.ApplyStatus(status);
            AdminPage.ApplyServerLink(status.Link);

            foreach (var page in _pages) await page.TickAsync().ConfigureAwait(true);

            UpdateIndicators();
        }
        catch (Exception ex)
        {
            // The clock must never die; a one-off failure is reported on the active page instead.
            AsyncRelayCommand.UnhandledError?.Invoke(ex);
        }
        finally
        {
            _ticking = false;
        }
    }

    private void UpdateIndicators()
    {
        NavItems[0].Indicator = ServerPage.RunState switch
        {
            ServerRunState.Running or ServerRunState.Incompatible => NavIndicator.Running,
            ServerRunState.Starting or ServerRunState.Stopping => NavIndicator.Pending,
            _ => NavIndicator.None,
        };

        NavItems[1].Indicator = AdminPage.IsRunning ? NavIndicator.Running : NavIndicator.None;

        NavItems[2].Indicator = VersionsPage.DeviceAttached ? NavIndicator.DeviceReady : NavIndicator.None;

        NavItems[3].Indicator = RecordingPage.IsActive
            ? NavIndicator.RecordingActive
            : RecordingPage.IsArmed
                ? NavIndicator.RecordingArmed
                : NavIndicator.None;
    }

    /// <summary>Closes server and admin on the operator's "close everything" choice.</summary>
    public async Task ShutdownEverythingAsync()
    {
        _timer.Stop();

        if (AdminPage.IsRunning) await _context.Admin.StopAsync().ConfigureAwait(true);

        // Same clean-then-forced stop as the button, so an open recording gets finalized.
        if (ServerPage.IsRunning) await ServerPage.StopForExitAsync().ConfigureAwait(true);
    }

    public void Dispose()
    {
        _timer.Stop();
        _context.Dispose();
    }
}
