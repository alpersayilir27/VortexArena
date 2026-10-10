using System.IO;

namespace VortexArena.Launcher.Services;

/// <summary>One unpacked build in a <c>&lt;kind&gt;_versions\</c> folder.</summary>
public sealed record LocalBuild(int Version, string Dir, string Exe, DateTime Modified);

/// <summary>One line of a build version list: the union of remote and unpacked.</summary>
public sealed record BuildVersionRow(
    int Version,
    bool OnRemote,
    bool Installed,
    long Size,
    string Modified,
    CloudState Cloud,
    RemoteVersion? Remote,
    string? Exe)
{
    /// <summary>Can be fetched: published and not unpacked here yet.</summary>
    public bool CanDownload => OnRemote && !Installed;

    /// <summary>Crossed-out cloud: we hold a build the server does not confirm.</summary>
    public bool CloudWarning => Installed && Cloud != CloudState.OnServer;
}

/// <summary>
/// Reads a <c>&lt;kind&gt;_versions\</c> folder and builds its version list — one implementation for
/// every versioned build (admin, server).
/// <para>⚠️ A folder counts as installed only with the package's exe in it: a half-unpacked folder
/// would look startable and the operator would get a broken build.</para>
/// </summary>
public static class BuildVersionCatalog
{
    /// <summary>Unpacked builds, newest first.</summary>
    public static IReadOnlyList<LocalBuild> ReadInstalled(string versionsDir, BuildPackage package)
    {
        try
        {
            Directory.CreateDirectory(versionsDir);

            var found = new List<LocalBuild>();
            foreach (var dir in new DirectoryInfo(versionsDir).GetDirectories())
            {
                var version = package.ParseFolderName(dir.Name);
                if (version is null) continue;

                var exe = Path.Combine(dir.FullName, package.ExeName);
                if (!File.Exists(exe)) continue;

                found.Add(new LocalBuild(version.Value, dir.FullName, exe, dir.LastWriteTime));
            }

            return found.OrderByDescending(b => b.Version).ToArray();
        }
        catch (Exception)
        {
            return [];
        }
    }

    /// <summary>
    /// Deletes zip, <c>.part</c> and <c>.extract</c> remains of failed installs.
    /// <para>⚠️ Only names this launcher produces are touched, and never a version in
    /// <paramref name="busy"/> — an in-flight download writes exactly those files.</para>
    /// </summary>
    public static void CleanLeftovers(string versionsDir, BuildPackage package, IReadOnlyCollection<int> busy)
    {
        try
        {
            var dir = new DirectoryInfo(versionsDir);
            if (!dir.Exists) return;

            foreach (var file in dir.GetFiles())
            {
                var name = file.Name.EndsWith(HttpVersionSource.PartialExtension, StringComparison.OrdinalIgnoreCase)
                    ? file.Name[..^HttpVersionSource.PartialExtension.Length]
                    : file.Name;

                var version = package.ParseFileName(name);
                if (version is null || busy.Contains(version.Value)) continue;

                TryDelete(() => file.Delete());
            }

            foreach (var sub in dir.GetDirectories("*" + BuildPackage.ExtractSuffix))
            {
                var version = package.ParseFolderName(sub.Name[..^BuildPackage.ExtractSuffix.Length]);
                if (version is null || busy.Contains(version.Value)) continue;

                TryDelete(() => sub.Delete(recursive: true));
            }
        }
        catch (Exception)
        {
            // Leftovers are never listed; a locked file is cleaned on the next refresh.
        }
    }

    /// <summary>
    /// Merges server listing and unpacked builds, newest first.
    /// </summary>
    /// <param name="remote">Server listing; <b>null means the list could not be fetched</b> — which
    /// is what separates "withdrawn" from "cannot tell".</param>
    public static IReadOnlyList<BuildVersionRow> Merge(
        IReadOnlyList<RemoteVersion>? remote,
        IReadOnlyList<LocalBuild> installed)
    {
        var remoteByVersion = remote?.ToDictionary(v => v.Version) ?? new Dictionary<int, RemoteVersion>();
        var installedByVersion = installed.ToDictionary(b => b.Version);

        var versions = new SortedSet<int>(remoteByVersion.Keys);
        versions.UnionWith(installedByVersion.Keys);

        var rows = new List<BuildVersionRow>();
        foreach (var version in versions.Reverse())
        {
            remoteByVersion.TryGetValue(version, out var remoteEntry);
            installedByVersion.TryGetValue(version, out var localEntry);

            var onRemote = remoteEntry is not null;
            var cloud = onRemote
                ? CloudState.OnServer
                : remote is null
                    ? CloudState.ServerUnreachable
                    : CloudState.MissingOnServer;

            var modified = remoteEntry is not null && remoteEntry.Modified.Length > 0
                ? remoteEntry.Modified
                : localEntry is not null
                    ? localEntry.Modified.ToString("yyyy-MM-dd HH:mm")
                    : "";

            rows.Add(new BuildVersionRow(
                version,
                onRemote,
                localEntry is not null,
                remoteEntry?.Size ?? 0,
                modified,
                cloud,
                remoteEntry,
                localEntry?.Exe));
        }

        return rows;
    }

    /// <summary>
    /// Version that will be started: the operator's pick when it is unpacked, else the highest
    /// unpacked one; 0 when nothing is installed. The fallback is NOT persisted.
    /// </summary>
    public static int Select(int preferred, IReadOnlyCollection<int> installed)
    {
        if (installed.Count == 0) return 0;
        return preferred > 0 && installed.Contains(preferred) ? preferred : installed.Max();
    }

    /// <summary>
    /// Version of an exe sitting in <c>&lt;versionsDir&gt;\&lt;prefix&gt;&lt;N&gt;\</c>; 0 for anything
    /// else — another deployment's build or a pre-version layout must not be claimed as ours.
    /// </summary>
    public static int VersionOfExe(string? exePath, string versionsDir, BuildPackage package)
    {
        if (string.IsNullOrEmpty(exePath)) return 0;
        if (!string.Equals(Path.GetFileName(exePath), package.ExeName, StringComparison.OrdinalIgnoreCase))
            return 0;

        var versionDir = Path.GetDirectoryName(exePath);
        if (versionDir is null) return 0;
        if (!string.Equals(Path.GetDirectoryName(versionDir), versionsDir, StringComparison.OrdinalIgnoreCase))
            return 0;

        return package.ParseFolderName(Path.GetFileName(versionDir)) ?? 0;
    }

    private static void TryDelete(Action delete)
    {
        try
        {
            delete();
        }
        catch (Exception)
        {
            // Locked by Explorer or an antivirus scan; retried on the next refresh.
        }
    }
}

/// <summary>
/// Cached set of unpacked builds of one package plus the "which one starts" rule.
/// <para>Cached on purpose: the pages ask on every tick and the scan hits the disk.</para>
/// </summary>
public sealed class InstalledBuilds
{
    private readonly string _versionsDir;
    private readonly BuildPackage _package;
    private readonly Func<int> _preferred;

    private IReadOnlyList<int> _versions = [];
    private bool _read;

    /// <param name="preferred">Operator's saved pick; 0 = automatic.</param>
    public InstalledBuilds(string versionsDir, BuildPackage package, Func<int> preferred)
    {
        _versionsDir = versionsDir;
        _package = package;
        _preferred = preferred;
    }

    /// <summary>Unpacked versions, newest first. Cached — <see cref="Refresh"/> re-reads the disk.</summary>
    public IReadOnlyList<int> Versions
    {
        get
        {
            if (!_read) Refresh();
            return _versions;
        }
    }

    /// <summary>Version that would be started; 0 when no build is unpacked.</summary>
    public int Selected => BuildVersionCatalog.Select(_preferred(), Versions);

    /// <summary>Re-reads the unpacked versions; true when the set changed.</summary>
    public bool Refresh()
    {
        var fresh = BuildVersionCatalog.ReadInstalled(_versionsDir, _package)
            .Select(b => b.Version)
            .ToArray();
        var changed = !_read || !fresh.SequenceEqual(_versions);
        _versions = fresh;
        _read = true;
        return changed;
    }
}
