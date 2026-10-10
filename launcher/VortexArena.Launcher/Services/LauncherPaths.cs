using System.IO;

namespace VortexArena.Launcher.Services;

/// <summary>
/// The deployment layout, resolved once at startup. Everything the launcher drives lives next to
/// the exe: <c>server\</c>, <c>server_versions\</c>, <c>admin_versions\</c>, <c>game_versions\</c>,
/// <c>replays\</c>, <c>platform-tools\</c>.
/// <para>⚠️ Exe paths are NOT settings. An operator who could point them anywhere could
/// also point them at a server whose <c>config\maps.json</c> belongs to another business.</para>
/// </summary>
public sealed class LauncherPaths
{
    /// <summary>Overrides root detection: <c>--root &lt;folder&gt;</c>.</summary>
    public const string ArgRoot = "--root";

    /// <summary>Firewall rule script shipped in the server zip.</summary>
    private const string FirewallScriptName = "firewall-kur.cmd";

    /// <summary>Root detection still accepts the single-folder layout that predates per-version folders.</summary>
    private const string LegacyAdminFolderName = "admin";

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

    /// <summary>Venue DATA folder: the server keeps its config, logs and survey files here
    /// (<c>--data-dir</c>), so a version upgrade never touches operator data.</summary>
    public string ServerDir => Path.Combine(Root, "server");

    /// <summary>Written by the server on first start, seeded from the version's own copy.</summary>
    public string ServerConfigJson => Path.Combine(ServerDir, "config", "server.json");

    public string ServerLogsDir => Path.Combine(ServerDir, "logs");

    /// <summary>One folder per server build: <c>server_versions\server_v&lt;N&gt;\</c>.</summary>
    public string ServerVersionsDir => Path.Combine(Root, "server_versions");

    public string ServerVersionDir(int version)
        => Path.Combine(ServerVersionsDir, BuildPackage.Server.FolderNameFor(version));

    public string ServerExeFor(int version)
        => Path.Combine(ServerVersionDir(version), BuildPackage.Server.ExeName);

    /// <summary>Map catalog of one build; the server reads it ONLY from the version folder.</summary>
    public string ServerMapsJsonFor(int version)
        => Path.Combine(ServerVersionDir(version), "config", "maps.json");

    /// <summary>Seed copy shipped with the build; the live one is <see cref="ServerConfigJson"/>.</summary>
    public string ServerVersionConfigJson(int version)
        => Path.Combine(ServerVersionDir(version), "config", "server.json");

    public string ServerFirewallScriptFor(int version)
        => Path.Combine(ServerVersionDir(version), FirewallScriptName);

    /// <summary>Pre-version location; only used to recognize an old server still running.</summary>
    public string LegacyServerExe => Path.Combine(ServerDir, BuildPackage.Server.ExeName);

    /// <summary>One folder per admin build: <c>admin_versions\admin_v&lt;N&gt;\</c>.</summary>
    public string AdminVersionsDir => Path.Combine(Root, "admin_versions");

    public string AdminVersionDir(int version)
        => Path.Combine(AdminVersionsDir, BuildPackage.Admin.FolderNameFor(version));

    public string AdminExeFor(int version)
        => Path.Combine(AdminVersionDir(version), BuildPackage.Admin.ExeName);

    public string GameVersionsDir => Path.Combine(Root, "game_versions");

    public string ReplaysDir => Path.Combine(Root, "replays");

    /// <summary>Android platform-tools; filled by the deploy script, not by the operator.</summary>
    public string PlatformToolsDir => Path.Combine(Root, "platform-tools");

    public string AdbExe => Path.Combine(PlatformToolsDir, "adb.exe");

    public bool ServerVersionInstalled(int version) => File.Exists(ServerExeFor(version));

    public bool AdminVersionInstalled(int version) => File.Exists(AdminExeFor(version));

    /// <summary>
    /// Resolution order: <c>--root</c> → exe folder holding <c>server\</c>/<c>admin_versions\</c> → first
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
        foreach (var dir in new[] { ServerDir, ServerVersionsDir, AdminVersionsDir, GameVersionsDir, ReplaysDir })
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
                   Directory.Exists(Path.Combine(dir, "server_versions")) ||
                   Directory.Exists(Path.Combine(dir, "admin_versions")) ||
                   Directory.Exists(Path.Combine(dir, LegacyAdminFolderName));
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
