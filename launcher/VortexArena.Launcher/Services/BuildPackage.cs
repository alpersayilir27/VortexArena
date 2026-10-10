using System.Globalization;

namespace VortexArena.Launcher.Services;

/// <summary>
/// Naming of one versioned Windows build package — the ONE copy on the launcher side.
/// <para>⚠️ Mirrors the publish side: the zip is <c>&lt;prefix&gt;&lt;N&gt;.zip</c> and its root holds
/// the build itself, so every version unpacks into its own <c>&lt;prefix&gt;&lt;N&gt;\</c> folder and
/// installing one never touches another.</para>
/// </summary>
/// <param name="FilePrefix">Zip and folder prefix (<c>admin_v</c>, <c>server_v</c>).</param>
/// <param name="ExeName">Exe whose presence makes an unpacked folder count as installed.</param>
public sealed record BuildPackage(string FilePrefix, string ExeName)
{
    public const string FileExtension = ".zip";

    /// <summary>Suffix of the staging folder; renamed to the final name only when complete.</summary>
    public const string ExtractSuffix = ".extract";

    /// <summary>Windows admin build: <c>admin_versions\admin_v&lt;N&gt;\VortexArena.exe</c>.</summary>
    public static BuildPackage Admin { get; } = new("admin_v", "VortexArena.exe");

    /// <summary>Match server build: <c>server_versions\server_v&lt;N&gt;\VortexArena.Server.App.exe</c>.</summary>
    public static BuildPackage Server { get; } = new("server_v", "VortexArena.Server.App.exe");

    public string FileNameFor(int version) => FolderNameFor(version) + FileExtension;

    public string FolderNameFor(int version)
        => FilePrefix + version.ToString(CultureInfo.InvariantCulture);

    public string ExtractFolderNameFor(int version) => FolderNameFor(version) + ExtractSuffix;

    /// <summary>Version out of <c>&lt;prefix&gt;&lt;N&gt;.zip</c>; null for anything else.</summary>
    public int? ParseFileName(string fileName)
    {
        if (!fileName.EndsWith(FileExtension, StringComparison.OrdinalIgnoreCase)) return null;
        return ParseFolderName(fileName[..^FileExtension.Length]);
    }

    /// <summary>Version out of <c>&lt;prefix&gt;&lt;N&gt;</c>; null for anything else (<c>.extract</c> included).</summary>
    public int? ParseFolderName(string folderName)
    {
        if (folderName.Length == 0) return null;
        if (!folderName.StartsWith(FilePrefix, StringComparison.OrdinalIgnoreCase)) return null;

        var digits = folderName[FilePrefix.Length..];
        if (digits.Length == 0 || !digits.All(char.IsAsciiDigit)) return null;

        return int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var version)
            ? version
            : null;
    }
}
