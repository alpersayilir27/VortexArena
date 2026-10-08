using System.IO;

namespace VortexArena.Launcher.Services;

/// <summary>An APK sitting in <c>game_versions\</c>.</summary>
public sealed record LocalApk(int Version, string Path, long Size, DateTime Modified);

/// <summary>Cloud state of one row — drives the crossed-out-cloud icon.</summary>
public enum CloudState
{
    /// <summary>Listed on the update server.</summary>
    OnServer,

    /// <summary>Local copy the server does not list (withdrawn version), list was readable.</summary>
    MissingOnServer,

    /// <summary>Local copy and no list at all right now (offline) — cannot tell.</summary>
    ServerUnreachable,
}

/// <summary>One line of the Versions page: the union of remote, local and installed.</summary>
public sealed record VersionRow(
    int Version,
    bool OnRemote,
    bool Local,
    bool Installed,
    long Size,
    string Modified,
    CloudState Cloud,
    RemoteVersion? Remote,
    string? LocalPath)
{
    /// <summary>Can be downloaded: published and not here yet.</summary>
    public bool CanDownload => OnRemote && !Local;

    /// <summary>Installable: either already local, or downloadable first.</summary>
    public bool CanInstall => Local || OnRemote;

    /// <summary>Crossed-out cloud: we hold a copy the server does not confirm.</summary>
    public bool CloudWarning => Local && Cloud != CloudState.OnServer;
}

/// <summary>
/// Builds the version list and reads the local folder.
/// <para>⚠️ <b>The union is deliberate:</b> a version only present on the headset still gets a row,
/// otherwise the operator cannot see what is actually installed there.</para>
/// </summary>
public static class VersionCatalog
{
    /// <summary>APKs in <c>game_versions\</c>; <c>.part</c> files are ignored.</summary>
    public static IReadOnlyList<LocalApk> ReadLocal(string gameVersionsDir)
    {
        try
        {
            Directory.CreateDirectory(gameVersionsDir);

            var found = new List<LocalApk>();
            foreach (var file in new DirectoryInfo(gameVersionsDir).GetFiles("*" + GamePackage.FileExtension))
            {
                var version = GamePackage.ParseFileName(file.Name);
                if (version is null) continue;
                found.Add(new LocalApk(version.Value, file.FullName, file.Length, file.LastWriteTime));
            }

            return found.OrderByDescending(a => a.Version).ToArray();
        }
        catch (Exception)
        {
            return [];
        }
    }

    /// <summary>
    /// Merges the three sources, newest first.
    /// </summary>
    /// <param name="remote">Server listing; <b>null means the list could not be fetched</b> — which
    /// is what separates "withdrawn" from "cannot tell".</param>
    public static IReadOnlyList<VersionRow> Merge(
        IReadOnlyList<RemoteVersion>? remote,
        IReadOnlyList<LocalApk> local,
        IReadOnlyCollection<int> installed)
    {
        var remoteByVersion = remote?.ToDictionary(v => v.Version) ?? new Dictionary<int, RemoteVersion>();
        var localByVersion = local.ToDictionary(a => a.Version);
        var installedSet = new HashSet<int>(installed);

        var versions = new SortedSet<int>(remoteByVersion.Keys);
        versions.UnionWith(localByVersion.Keys);
        versions.UnionWith(installedSet);

        var rows = new List<VersionRow>();
        foreach (var version in versions.Reverse())
        {
            remoteByVersion.TryGetValue(version, out var remoteEntry);
            localByVersion.TryGetValue(version, out var localEntry);

            var onRemote = remoteEntry is not null;
            var isLocal = localEntry is not null;

            var cloud = onRemote
                ? CloudState.OnServer
                : remote is null
                    ? CloudState.ServerUnreachable
                    : CloudState.MissingOnServer;

            var size = localEntry?.Size ?? remoteEntry?.Size ?? 0;
            var modified = remoteEntry is not null && remoteEntry.Modified.Length > 0
                ? remoteEntry.Modified
                : localEntry is not null
                    ? localEntry.Modified.ToString("yyyy-MM-dd HH:mm")
                    : "";

            rows.Add(new VersionRow(
                version,
                onRemote,
                isLocal,
                installedSet.Contains(version),
                size,
                modified,
                cloud,
                remoteEntry,
                localEntry?.Path));
        }

        return rows;
    }
}

/// <summary>adb install failure codes the launcher reacts to by name.</summary>
public static class AdbInstallError
{
    /// <summary>Same package id, different signing key — only a targeted uninstall clears it.</summary>
    public const string UpdateIncompatible = "INSTALL_FAILED_UPDATE_INCOMPATIBLE";

    public const string InsufficientStorage = "INSTALL_FAILED_INSUFFICIENT_STORAGE";

    /// <summary>Operator-facing message; null when the raw adb output should be shown as is.</summary>
    public static string? Translate(string output)
    {
        if (output.Contains(InsufficientStorage, StringComparison.Ordinal))
            return "Gözlükte yer yok.";

        return null;
    }

    public static bool IsSignatureConflict(string output)
        => output.Contains(UpdateIncompatible, StringComparison.Ordinal);
}
