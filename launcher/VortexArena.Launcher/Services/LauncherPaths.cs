using System.IO;

namespace VortexArena.Launcher.Services;

/// <summary>
/// The deployment layout, resolved once at startup. Everything the launcher drives lives next to
/// the exe: <c>server\</c>, <c>admin\</c>, <c>game_versions\</c>, <c>replays\</c>.
/// <para>⚠️ Exe paths are NOT settings. An operator who could point them anywhere could
/// also point them at a server whose <c>config\maps.json</c> belongs to another business.</para>
/// </summary>
public sealed class LauncherPaths
{
    /// <summary>Overrides root detection: <c>--root &lt;folder&gt;</c>.</summary>
    public const string ArgRoot = "--root";

    private const string ServerExeName = "VortexArena.Server.App.exe";
    private const string AdminExeName = "VortexArena.exe";

    /// <summary>Package folder the deploy script fills: <c>deploy\launcher\</c>.</summary>
    private const string DeployFolderName = "launcher";

    /// <summary>How many levels above the exe the development-layout search walks up.</summary>
    private const int SearchDepth = 6;

    private LauncherPaths(string root, string source)
    {
        Root = root;
        Source = source;
    }

    public string Root { get; }

    /// <summary>How the root was found — shown in the sidebar tooltip.</summary>
    public string Source { get; }

    public string ServerDir => Path.Combine(Root, "server");

    public string ServerExe => Path.Combine(ServerDir, ServerExeName);

    public string ServerConfigJson => Path.Combine(ServerDir, "config", "server.json");

    public string ServerMapsJson => Path.Combine(ServerDir, "config", "maps.json");

    public string ServerLogsDir => Path.Combine(ServerDir, "logs");

    public string AdminDir => Path.Combine(Root, "admin");

    public string AdminExe => Path.Combine(AdminDir, AdminExeName);

    public string GameVersionsDir => Path.Combine(Root, "game_versions");

    public string ReplaysDir => Path.Combine(Root, "replays");

    public bool ServerExeExists => File.Exists(ServerExe);

    public bool AdminExeExists => File.Exists(AdminExe);

    public bool ServerMapsJsonExists => File.Exists(ServerMapsJson);

    /// <summary>
    /// Resolution order: <c>--root</c> → exe folder holding <c>server\</c>/<c>admin\</c> → first
    /// <c>deploy\launcher\</c> with that layout up to <see cref="SearchDepth"/> levels above (repo
    /// checkout, <c>dotnet run</c>) → exe folder.
    /// <para>⚠️ The exe folder comes from <see cref="AppContext.BaseDirectory"/>:
    /// <c>Assembly.Location</c> is EMPTY in a single-file build.</para>
    /// </summary>
    public static LauncherPaths Resolve(IReadOnlyList<string> args)
        => Resolve(args, AppContext.BaseDirectory);

    /// <summary>Same as <see cref="Resolve(IReadOnlyList{string})"/> with an explicit exe folder.</summary>
    public static LauncherPaths Resolve(IReadOnlyList<string> args, string exeDir)
    {
        var explicitRoot = ReadRootArgument(args);
        if (explicitRoot != null) return new LauncherPaths(explicitRoot, ArgRoot);

        var baseDir = TrimSeparator(exeDir);

        if (HasLayout(baseDir)) return new LauncherPaths(baseDir, "exe klasörü");

        var dir = baseDir;
        for (int i = 0; i < SearchDepth && !string.IsNullOrEmpty(dir); i++)
        {
            var deployed = Path.Combine(dir, "deploy", DeployFolderName);
            if (HasLayout(deployed)) return new LauncherPaths(deployed, "geliştirme: deploy\\launcher\\");
            dir = Path.GetDirectoryName(dir);
        }

        return new LauncherPaths(baseDir, "exe klasörü (yeni kurulum)");
    }

    /// <summary>
    /// Creates the empty layout folders so the operator sees where each build goes.
    /// <para>Best effort: an unwritable root still opens; pages show what is missing.</para>
    /// </summary>
    public void EnsureLayout()
    {
        foreach (var dir in new[] { ServerDir, AdminDir, GameVersionsDir, ReplaysDir })
        {
            try
            {
                Directory.CreateDirectory(dir);
            }
            catch (Exception)
            {
                // Read-only location; the missing-build messages cover it.
            }
        }
    }

    private static string? ReadRootArgument(IReadOnlyList<string> args)
    {
        for (int i = 0; i < args.Count - 1; i++)
        {
            if (!string.Equals(args[i], ArgRoot, StringComparison.OrdinalIgnoreCase)) continue;

            try
            {
                return TrimSeparator(Path.GetFullPath(args[i + 1]));
            }
            catch (Exception)
            {
                return null;
            }
        }

        return null;
    }

    private static bool HasLayout(string dir)
    {
        try
        {
            return Directory.Exists(Path.Combine(dir, "server")) ||
                   Directory.Exists(Path.Combine(dir, "admin"));
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static string TrimSeparator(string path)
        => path.Length > 3 ? path.TrimEnd(Path.DirectorySeparatorChar) : path;

    /// <summary>Shortens a path for the sidebar; the full value belongs in the tooltip.</summary>
    public static string Shorten(string path, int maxLength = 34)
    {
        if (path.Length <= maxLength) return path;
        return "…" + path[^(maxLength - 1)..];
    }
}
