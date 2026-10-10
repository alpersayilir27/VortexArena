namespace VortexArena.Launcher.Services;

/// <summary>
/// Everything the pages share: layout, settings and the long-lived services.
/// <para>Composed once in <c>MainViewModel</c>; there is no container and no DI framework.</para>
/// </summary>
public sealed class LauncherContext : IDisposable
{
    public LauncherContext(LauncherPaths paths, LauncherSettings settings)
    {
        Paths = paths;
        Settings = settings;
        StatusClient = new ServerStatusClient();
        Server = new ServerController(paths, settings);
        Admin = new AdminController(paths, settings);
        Adb = new AdbService(paths.AdbExe);
        Versions = new HttpVersionSource(
            () => Settings.VersionsUrl, () => Settings.DownloadBaseUrl, GamePackage.FileNameFor);
        AdminVersions = new HttpVersionSource(
            () => Settings.AdminVersionsUrl, () => Settings.AdminDownloadBaseUrl,
            BuildPackage.Admin.FileNameFor);
        ServerVersions = new HttpVersionSource(
            () => Settings.ServerVersionsUrl, () => Settings.ServerDownloadBaseUrl,
            BuildPackage.Server.FileNameFor);
        RefreshControlPort();
    }

    public LauncherPaths Paths { get; }

    public LauncherSettings Settings { get; }

    public ServerStatusClient StatusClient { get; }

    public ServerController Server { get; }

    public AdminController Admin { get; }

    public AdbService Adb { get; }

    /// <summary>Player APK feed.</summary>
    public IVersionSource Versions { get; }

    /// <summary>Windows admin build feed; separate list and download base.</summary>
    public IVersionSource AdminVersions { get; }

    /// <summary>Match server build feed; separate list and download base.</summary>
    public IVersionSource ServerVersions { get; }

    /// <summary>Control port in use: override → <c>server.json</c> → protocol default.</summary>
    public int ControlPort { get; private set; }

    /// <summary>
    /// Re-resolves the control port. The venue data config comes first; it does not exist before the
    /// server's first start, so the selected build's seed copy answers until then.
    /// </summary>
    public void RefreshControlPort()
    {
        var version = Server.SelectedVersion;
        ControlPort = Settings.ResolveControlPort(
            Paths.ServerConfigJson,
            version > 0 ? Paths.ServerVersionConfigJson(version) : "");
    }

    /// <summary>Persists settings; a failure must not take the launcher down with it.</summary>
    public void SaveSettings()
    {
        try
        {
            Settings.Save();
        }
        catch (Exception)
        {
            // Read-only profile; the session keeps working with in-memory values.
        }
    }

    public void Dispose()
    {
        StatusClient.Dispose();
        (Versions as IDisposable)?.Dispose();
        (AdminVersions as IDisposable)?.Dispose();
        (ServerVersions as IDisposable)?.Dispose();
    }
}
