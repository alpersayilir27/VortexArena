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
        Server = new ServerController(paths);
        Admin = new AdminController(paths, settings);
        Adb = new AdbService();
        Versions = new HttpVersionSource(() => Settings.VersionsUrl, () => Settings.DownloadBaseUrl);
        RefreshControlPort();
    }

    public LauncherPaths Paths { get; }

    public LauncherSettings Settings { get; }

    public ServerStatusClient StatusClient { get; }

    public ServerController Server { get; }

    public AdminController Admin { get; }

    public AdbService Adb { get; }

    public IVersionSource Versions { get; }

    /// <summary>Control port in use: override → <c>server.json</c> → protocol default.</summary>
    public int ControlPort { get; private set; }

    public void RefreshControlPort() => ControlPort = Settings.ResolveControlPort(Paths.ServerConfigJson);

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
    }
}
