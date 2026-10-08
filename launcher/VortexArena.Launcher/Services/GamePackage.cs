namespace VortexArena.Launcher.Services;

/// <summary>
/// Naming of the player APK and its Android package — the ONE copy on the launcher side.
/// <para>⚠️ Mirrors Unity's <c>PlayerBuildTool</c>: the version is part of the package id
/// (<c>com.vortex.arenav132</c>), which is exactly why several versions live side by side on one
/// headset and installing a new one never removes another.</para>
/// </summary>
public static class GamePackage
{
    public const string Prefix = "com.vortex.arenav";

    public const string FilePrefix = "game_v";

    public const string FileExtension = ".apk";

    public static string NameFor(int version) => Prefix + version.ToString(System.Globalization.CultureInfo.InvariantCulture);

    public static string FileNameFor(int version)
        => FilePrefix + version.ToString(System.Globalization.CultureInfo.InvariantCulture) + FileExtension;

    /// <summary>Version out of <c>game_v&lt;N&gt;.apk</c>; null for anything else.</summary>
    public static int? ParseFileName(string fileName)
    {
        if (fileName.Length == 0) return null;
        if (!fileName.StartsWith(FilePrefix, StringComparison.OrdinalIgnoreCase)) return null;
        if (!fileName.EndsWith(FileExtension, StringComparison.OrdinalIgnoreCase)) return null;

        var digits = fileName[FilePrefix.Length..^FileExtension.Length];
        if (digits.Length == 0 || !digits.All(char.IsAsciiDigit)) return null;

        return int.TryParse(digits, System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture, out var version)
            ? version
            : null;
    }
}
