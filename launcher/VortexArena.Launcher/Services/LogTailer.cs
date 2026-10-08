using System.IO;
using System.Text;

namespace VortexArena.Launcher.Services;

/// <summary>
/// Follows the newest <c>server\logs\server-*.log</c> incrementally.
/// <para>⚠️ Opened with <see cref="FileShare.ReadWrite"/>: the server holds the file open for
/// writing, and a plain <c>Read</c> share would fail every tick.</para>
/// </summary>
public sealed class LogTailer
{
    private const string Pattern = "server-*.log";

    private readonly string _logsDir;
    private string? _file;
    private long _offset;
    private string _partial = "";

    public LogTailer(string logsDir) => _logsDir = logsDir;

    /// <summary>File currently followed; null when the folder has no log yet.</summary>
    public string? CurrentFile => _file;

    /// <summary>True when the tailer switched files on the last read (the view should clear).</summary>
    public bool FileChanged { get; private set; }

    /// <summary>Newest log by last write time; null when none.</summary>
    public static string? FindNewestLog(string logsDir)
    {
        try
        {
            if (!Directory.Exists(logsDir)) return null;

            return new DirectoryInfo(logsDir)
                .GetFiles(Pattern)
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .Select(f => f.FullName)
                .FirstOrDefault();
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Last <paramref name="lineCount"/> lines of a log, for error messages.</summary>
    public static string ReadTail(string? path, int lineCount)
    {
        if (path is null) return "";

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var ring = new Queue<string>(lineCount);
            while (reader.ReadLine() is { } line)
            {
                if (ring.Count == lineCount) ring.Dequeue();
                ring.Enqueue(line);
            }

            return string.Join(Environment.NewLine, ring);
        }
        catch (Exception)
        {
            return "";
        }
    }

    /// <summary>
    /// Lines written since the previous call. Switching to a newer log, or a file that shrank
    /// (rotated/replaced), restarts from the beginning and sets <see cref="FileChanged"/>.
    /// </summary>
    public IReadOnlyList<string> ReadNew()
    {
        FileChanged = false;

        var newest = FindNewestLog(_logsDir);
        if (newest is null)
        {
            if (_file is not null) Reset(null);
            return [];
        }

        if (!string.Equals(newest, _file, StringComparison.OrdinalIgnoreCase)) Reset(newest);

        try
        {
            using var stream = new FileStream(newest, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (stream.Length < _offset) Reset(newest);
            if (stream.Length == _offset) return [];

            stream.Position = _offset;
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var text = _partial + reader.ReadToEnd();
            _offset = stream.Length;

            var lines = text.Split('\n');

            // A tail without a newline is half a line; keep it for the next tick.
            _partial = lines[^1];
            return lines.Take(lines.Length - 1).Select(l => l.TrimEnd('\r')).ToArray();
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
    }

    private void Reset(string? file)
    {
        _file = file;
        _offset = 0;
        _partial = "";
        FileChanged = true;
    }
}
