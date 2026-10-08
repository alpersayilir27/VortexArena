using System.IO;
using System.Text;
using System.Text.Json;
using VortexArena.Protocol;

namespace VortexArena.Launcher.Services;

/// <summary>One <c>.vxr</c> file as the list shows it.</summary>
public sealed record ReplayEntry(
    string Path,
    string FileName,
    DateTime Started,
    string SceneName,
    string ModeId,
    TimeSpan Duration,
    bool Finalized,
    int PlayerCount,
    long Size,
    string? Error);

/// <summary>
/// Reads the replay folder listing.
/// <para>⚠️ <b>Header + meta only</b> — a match file runs to hundreds of MB and the records carry
/// nothing the list needs. Opened with <see cref="FileShare.ReadWrite"/> so the file the server is
/// writing right now can be listed too.</para>
/// </summary>
public static class ReplayLibrary
{
    private static readonly JsonSerializerOptions JsonOptions = new() { IncludeFields = true };

    /// <summary>Newest first; unreadable files stay in the list carrying their error.</summary>
    public static IReadOnlyList<ReplayEntry> Read(string directory)
    {
        try
        {
            if (!Directory.Exists(directory)) return [];

            return new DirectoryInfo(directory)
                .GetFiles("*" + ReplayFile.EXTENSION)
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .Select(ReadOne)
                .ToArray();
        }
        catch (Exception)
        {
            return [];
        }
    }

    private static ReplayEntry ReadOne(FileInfo file)
    {
        try
        {
            using var stream = new FileStream(
                file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new BinaryReader(stream, Encoding.UTF8);

            if (!ReplayFile.TryReadHeader(reader, out var header, out var metaJson, out var error))
                return Failed(file, error ?? "Kayıt dosyası okunamadı.");

            ReplayMeta? meta = null;
            try
            {
                meta = JsonSerializer.Deserialize<ReplayMeta>(metaJson, JsonOptions);
            }
            catch (JsonException)
            {
                // Meta is cosmetic; a bad one must not hide the file.
            }

            return new ReplayEntry(
                file.FullName,
                file.Name,
                DateTimeOffset.FromUnixTimeMilliseconds(header.startedUnixMs).ToLocalTime().DateTime,
                meta?.sceneName ?? "",
                meta?.modeId ?? "",
                TimeSpan.FromMilliseconds(header.durationMs),
                header.IsFinalized,
                meta?.players?.Length ?? 0,
                file.Length,
                null);
        }
        catch (Exception ex)
        {
            return Failed(file, ex.Message);
        }
    }

    private static ReplayEntry Failed(FileInfo file, string error) => new(
        file.FullName, file.Name, file.LastWriteTime, "", "", TimeSpan.Zero, false, 0,
        SafeLength(file), error);

    private static long SafeLength(FileInfo file)
    {
        try
        {
            return file.Length;
        }
        catch (Exception)
        {
            return 0;
        }
    }
}
