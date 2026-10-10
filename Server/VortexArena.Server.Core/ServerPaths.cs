#nullable enable
namespace VortexArena.Server.Core;

/// <summary>Resolved file locations of one run: where config, logs and venue data are read/written.</summary>
/// <remarks>⚠️ Two layouts. Without <c>--data-dir</c> everything sits next to the exe (dev run and a
/// hand-copied single folder). With <c>--data-dir</c> the venue's own data (server.json,
/// devices.json, logs, venue survey) lives OUTSIDE the version folder, so installing another server
/// version does not take it along — but <c>maps.json</c> keeps coming from the version folder: it
/// ships with the build and must match the player/admin builds of the same version.</remarks>
public sealed class ServerPaths
{
    private ServerPaths(string exeDir, string dataDir, string configDir, string mapsPath,
        string logDir, string surveyDir, bool external)
    {
        ExeDir = exeDir;
        DataDir = dataDir;
        ConfigDir = configDir;
        MapsPath = mapsPath;
        LogDir = logDir;
        SurveyDir = surveyDir;
        IsExternalDataDir = external;
    }

    /// <summary>Folder of the running exe (the version folder in a venue install).</summary>
    public string ExeDir { get; }

    /// <summary>Root of the data that survives a version change; equals <see cref="ExeDir"/> without
    /// <c>--data-dir</c>.</summary>
    public string DataDir { get; }

    /// <summary>Holds server.json and devices.json.</summary>
    public string ConfigDir { get; }

    /// <summary>Full path of maps.json (always the version's own copy).</summary>
    public string MapsPath { get; }

    /// <summary>Session log folder.</summary>
    public string LogDir { get; }

    /// <summary>Where <c>&lt;Venue&gt;_dimensions.json</c> is written.</summary>
    public string SurveyDir { get; }

    /// <summary><c>--data-dir</c> was given.</summary>
    public bool IsExternalDataDir { get; }

    /// <summary>Resolves the layout; <c>null</c> with the reason in <paramref name="error"/> when the
    /// data dir is unusable (startup must then fail — a server writing somewhere else is worse).</summary>
    /// <param name="dataDir">Value of <c>--data-dir</c>; null/empty = exe-local layout.</param>
    public static ServerPaths? Resolve(string? dataDir, out string error)
    {
        error = "";
        var exeDir = TrimSeparator(AppContext.BaseDirectory);

        if (string.IsNullOrWhiteSpace(dataDir))
        {
            try
            {
                var configDir = FindExeLocalConfigDir(exeDir);
                return new ServerPaths(exeDir, exeDir, configDir,
                    Path.Combine(configDir, "maps.json"),
                    Path.Combine(exeDir, "logs"), exeDir, external: false);
            }
            catch (Exception ex)
            {
                error = $"config klasörü hazırlanamadı ({exeDir}): {ex.Message}";
                return null;
            }
        }

        string root;
        try
        {
            root = TrimSeparator(Path.GetFullPath(dataDir.Trim()));
        }
        catch (Exception ex)
        {
            error = $"--data-dir yolu çözümlenemedi ('{dataDir}'): {ex.Message}";
            return null;
        }

        var dataConfig = Path.Combine(root, "config");
        var dataLogs = Path.Combine(root, "logs");
        try
        {
            Directory.CreateDirectory(root);
            Directory.CreateDirectory(dataConfig);
            Directory.CreateDirectory(dataLogs);
        }
        catch (Exception ex)
        {
            error = $"--data-dir klasörü oluşturulamadı ({root}): {ex.Message}";
            return null;
        }

        return new ServerPaths(exeDir, root, dataConfig,
            Path.Combine(exeDir, "config", "maps.json"),
            dataLogs, root, external: true);
    }

    /// <summary>Copies the version's server.json into an empty data dir (fresh venue seed); true when
    /// a copy happened.</summary>
    /// <remarks>⚠️ An existing data-dir server.json is NEVER overwritten: it is the venue's own
    /// setting, the version's copy is only a starting point. A failure is not fatal — ServerConfig
    /// then writes its defaults.</remarks>
    public bool TrySeedServerConfig(out string error)
    {
        error = "";
        if (!IsExternalDataDir) return false;

        var target = Path.Combine(ConfigDir, "server.json");
        if (File.Exists(target)) return false;

        var source = Path.Combine(ExeDir, "config", "server.json");
        if (!File.Exists(source)) return false;

        try
        {
            File.Copy(source, target);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>Walks up to 6 levels from the exe for config/server.json, else creates config/ next to
    /// the exe.</summary>
    /// <remarks>Needed because `dotnet run` runs from bin/Debug/… while the real files live under
    /// Server/config/.</remarks>
    private static string FindExeLocalConfigDir(string exeDir)
    {
        var dir = new DirectoryInfo(exeDir);
        for (var level = 0; level <= 6 && dir != null; level++, dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "config");
            if (File.Exists(Path.Combine(candidate, "server.json")))
                return candidate;
        }

        var fallback = Path.Combine(exeDir, "config");
        Directory.CreateDirectory(fallback);
        return fallback;
    }

    /// <summary>AppContext.BaseDirectory ends with a separator; Path.Combine results would carry it
    /// into printed paths.</summary>
    private static string TrimSeparator(string path) =>
        path.Length > 3 ? path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) : path;
}
