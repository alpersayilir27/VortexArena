#nullable enable
using System.Diagnostics;
using System.Text;
using System.Threading.Channels;
using VortexArena.Protocol;

namespace VortexArena.Server.Core;

/// <summary>Writes one <c>.vxr</c> file per match (§12): every admin-bound WS text and every outbound
/// UDP datagram, timestamped. Disarmed at startup — the operator arms it at runtime (§13).
/// <para>⚠️ Producers (match tick, snapshot loop, lobby broadcasts) may hold their own locks, so the
/// public methods ONLY enqueue: no I/O, no blocking, no exception escapes to the caller. A single
/// background task owns the file.</para></summary>
public sealed class MatchRecorder
{
    /// <summary>Wiring placeholder so call sites never need a null check: it can never be armed and
    /// owns no writer task.</summary>
    public static readonly MatchRecorder Disabled = new();

    private const byte KindBegin = 0; // not a wire kind: tells the writer to open a new file

    private readonly struct Item
    {
        public Item(int session, byte kind, uint timeMs, byte[]? payload, ReplayMeta? meta,
            string? fileName, long startedUnixMs)
        {
            Session = session;
            Kind = kind;
            TimeMs = timeMs;
            Payload = payload;
            Meta = meta;
            FileName = fileName;
            StartedUnixMs = startedUnixMs;
        }

        public readonly int Session;
        public readonly byte Kind;
        public readonly uint TimeMs;
        public readonly byte[]? Payload;
        public readonly ReplayMeta? Meta;
        public readonly string? FileName;
        public readonly long StartedUnixMs;
    }

    private readonly bool _wired;
    private readonly string _directory;
    private readonly int _keepDays;

    private readonly Channel<Item>? _channel;
    private readonly Task? _writer;

    /// <summary>Guards the producer-side session state; never held across I/O.</summary>
    private readonly object _gate = new();

    private int _session;
    private long _baseStamp;
    private volatile bool _recording;
    private volatile bool _armed;
    private volatile string _openFileName = "";

    public MatchRecorder(ServerConfig config)
    {
        _wired = true;
        _directory = ResolveDirectory(config.replayDir);
        _keepDays = Math.Max(0, config.replayKeepDays);

        // Queue and writer exist from the start even though recording begins disarmed (§12.2): the
        // startup prune runs in the writer, and arming mid-match must not race a task being created.
        _channel = Channel.CreateUnbounded<Item>(new UnboundedChannelOptions
        {
            SingleReader = true,
            AllowSynchronousContinuations = false
        });
        _writer = Task.Run(WriteLoopAsync);
    }

    private MatchRecorder()
    {
        _directory = "";
    }

    /// <summary>Builds the lobby/selection/admin snapshot a late-joining admin would get (§12.2).
    /// Wired by <see cref="LobbyService"/>, which owns those builders and is constructed after the
    /// director — hence a delegate rather than a ctor dependency.</summary>
    public Func<IReadOnlyList<string>>? InitialStateProvider { get; set; }

    public string Directory => _directory;

    /// <summary>Operator switch (§13); the server always starts disarmed.</summary>
    public bool Armed => _armed;

    /// <summary>Cheap enough to ask per message/datagram.</summary>
    public bool IsRecording => _recording;

    /// <summary>Arms recording; <c>true</c> only on the off→on edge, so the caller knows it has to open
    /// a file for a match that is already running (§12.2).</summary>
    public bool Arm()
    {
        if (!_wired) return false;
        lock (_gate)
        {
            if (_armed) return false;
            _armed = true;
            return true;
        }
    }

    /// <summary>Disarms and closes the open file (§12.2 <c>stopped</c>).</summary>
    public void Disarm()
    {
        if (!_wired) return;
        lock (_gate)
        {
            _armed = false;
            if (_recording) EndLocked(ReplayEndReason.Stopped);
        }
    }

    /// <summary>Recording block of <c>LauncherStatus</c> (§13), consistent in one snapshot.</summary>
    public LauncherRecordingStatus BuildStatus()
    {
        lock (_gate)
        {
            return new LauncherRecordingStatus
            {
                armed = _armed,
                active = _recording,
                file = _recording ? _openFileName : "",
                elapsedMs = _recording ? ElapsedMs() : 0,
                directory = _directory
            };
        }
    }

    /// <summary>The initial state for <see cref="Begin"/>; empty when nothing is wired or the builder
    /// throws (a broken snapshot must not block the match).</summary>
    public IReadOnlyList<string> CaptureInitialState()
    {
        var provider = InitialStateProvider;
        if (!_wired || provider == null) return Array.Empty<string>();
        try { return provider() ?? Array.Empty<string>(); }
        catch (Exception ex)
        {
            Console.WriteLine($"[Kayıt] Başlangıç durumu alınamadı: {ex.Message}");
            return Array.Empty<string>();
        }
    }

    /// <summary>Opens a recording (§12.2): <c>initialTexts</c> is the state a late-joining admin would
    /// get, written before anything else.</summary>
    public void Begin(ReplayMeta meta, IReadOnlyList<string>? initialTexts)
    {
        if (!_wired) return;

        lock (_gate)
        {
            if (!_armed) return;
            // A match starting over an open recording closes the old one (§12.2).
            if (_recording) EndLocked(ReplayEndReason.Restart);

            _session++;
            _baseStamp = Stopwatch.GetTimestamp();
            _recording = true;
            var fileName = BuildFileName(meta);
            // Candidate name until the writer reports the real one (a same-second collision adds _2).
            _openFileName = fileName + ReplayFile.EXTENSION;
            Enqueue(new Item(_session, KindBegin, 0, null, meta, fileName,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));

            // Inside the lock: a concurrent datagram must not land before the initial state.
            if (initialTexts == null) return;
            foreach (var json in initialTexts) Text(json);
        }
    }

    /// <summary>One admin-bound WS message (§12.1) — the single copy, not one per admin.</summary>
    public void Text(string json)
    {
        if (!_recording || string.IsNullOrEmpty(json)) return;
        Append(ReplayRecordKind.Text, Encoding.UTF8.GetBytes(json));
    }

    /// <summary>One outbound UDP datagram (§12.1). The array is taken BY REFERENCE — the snapshot loop
    /// builds a fresh buffer per packet, so it must not be reused after this call.</summary>
    public void Datagram(byte[] bytes)
    {
        if (!_recording || bytes.Length == 0) return;
        Append(ReplayRecordKind.Datagram, bytes);
    }

    /// <summary>Closes the open recording; a no-op when none is open.</summary>
    public void End(string reason)
    {
        if (!_wired) return;
        lock (_gate)
        {
            if (!_recording) return;
            EndLocked(reason);
        }
    }

    /// <summary>Drains the queue and closes the file; the caller's shutdown budget is the ceiling.</summary>
    public async Task StopAsync(TimeSpan timeout)
    {
        if (_channel == null || _writer == null) return;

        _channel.Writer.TryComplete();
        if (await Task.WhenAny(_writer, Task.Delay(timeout)) != _writer)
        {
            Console.WriteLine("[Kayıt] Yazıcı zamanında durmadı — kayıt yarım kalabilir.");
            return;
        }

        try { await _writer; }
        catch (Exception ex) { Console.WriteLine($"[Kayıt] Yazıcı hatayla bitti: {ex.Message}"); }
    }

    private void EndLocked(string reason)
    {
        _recording = false;
        Enqueue(new Item(_session, ReplayRecordKind.End, ElapsedMs(),
            Encoding.UTF8.GetBytes(reason), null, null, 0));
    }

    private void Append(byte kind, byte[] payload)
    {
        // Oversized payloads are unreadable by contract (§12.3) — dropping one record keeps the rest
        // of the file valid.
        if (payload.Length > ReplayFile.MAX_PAYLOAD_BYTES) return;
        lock (_gate)
        {
            if (!_recording) return;
            Enqueue(new Item(_session, kind, ElapsedMs(), payload, null, null, 0));
        }
    }

    private void Enqueue(Item item) => _channel?.Writer.TryWrite(item);

    /// <summary>Monotonic ms since the open; the wall clock is too coarse at 20 Hz.</summary>
    private uint ElapsedMs()
    {
        var ticks = Stopwatch.GetTimestamp() - _baseStamp;
        if (ticks <= 0) return 0;
        var ms = ticks * 1000d / Stopwatch.Frequency;
        return ms >= uint.MaxValue ? uint.MaxValue : (uint)ms;
    }

    // ---- Writer side (single task, the only owner of the file) ----

    private async Task WriteLoopAsync()
    {
        var reader = _channel!.Reader;

        PruneOld();

        FileStream? stream = null;
        BinaryWriter? writer = null;
        string path = "";
        int openSession = -1;
        uint recordCount = 0;
        uint lastTimeMs = 0;
        var lastFlush = Stopwatch.GetTimestamp();

        while (await reader.WaitToReadAsync().ConfigureAwait(false))
        {
            while (reader.TryRead(out var item))
            {
                if (item.Kind == KindBegin)
                {
                    CloseFile(ref stream, ref writer, path, recordCount, lastTimeMs, "yarıda");
                    openSession = -1;
                    recordCount = 0;
                    lastTimeMs = 0;

                    if (!TryOpenFile(item, out stream, out writer, out path)) continue;
                    PublishOpenName(item.Session, Path.GetFileName(path));
                    openSession = item.Session;
                    lastFlush = Stopwatch.GetTimestamp();
                    continue;
                }

                // Stray item from an abandoned (failed or already closed) session.
                if (writer == null || item.Session != openSession) continue;

                try
                {
                    ReplayFile.WriteRecord(writer, item.TimeMs, item.Kind, item.Payload!);
                    recordCount++;
                    lastTimeMs = item.TimeMs;
                }
                catch (Exception ex)
                {
                    ReportFailure(path, ex);
                    AbandonFile(ref stream, ref writer);
                    openSession = -1;
                    continue;
                }

                if (item.Kind == ReplayRecordKind.End)
                {
                    CloseFile(ref stream, ref writer, path, recordCount, lastTimeMs, null);
                    openSession = -1;
                    PruneOld();
                    continue;
                }

                if (DueForFlush(ref lastFlush)) SafeFlush(stream);
            }

            if (DueForFlush(ref lastFlush)) SafeFlush(stream);
        }

        // Channel completed without an End (hard shutdown): leave the file unfinalized — a reader
        // accepts it and plays up to the last whole record (§12.3).
        if (stream != null)
        {
            SafeFlush(stream);
            AbandonFile(ref stream, ref writer);
        }
    }

    /// <summary>Corrects the status name to the one actually opened; skipped once a newer session took
    /// over, so a late writer cannot overwrite a fresher name.</summary>
    private void PublishOpenName(int session, string fileName)
    {
        lock (_gate)
        {
            if (_recording && session == _session) _openFileName = fileName;
        }
    }

    private static bool DueForFlush(ref long lastFlush)
    {
        var now = Stopwatch.GetTimestamp();
        if (now - lastFlush < Stopwatch.Frequency) return false;
        lastFlush = now;
        return true;
    }

    private static void SafeFlush(FileStream? stream)
    {
        if (stream == null) return;
        try { stream.Flush(); }
        catch (Exception) { /* the next write reports it */ }
    }

    private bool TryOpenFile(Item item, out FileStream? stream, out BinaryWriter? writer, out string path)
    {
        stream = null;
        writer = null;
        path = "";
        try
        {
            System.IO.Directory.CreateDirectory(_directory);
            path = UniquePath(item.FileName ?? "match");
            stream = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read);
            writer = new BinaryWriter(stream, Encoding.UTF8, true);
            var meta = Encoding.UTF8.GetBytes(JsonUtil.Serialize(item.Meta ?? new ReplayMeta()));
            ReplayFile.WriteHeader(writer, item.StartedUnixMs, meta);
            Console.WriteLine($"[Kayıt] Başladı: {Path.GetFileName(path)}");
            return true;
        }
        catch (Exception ex)
        {
            ReportFailure(path.Length > 0 ? path : _directory, ex);
            AbandonFile(ref stream, ref writer);
            return false;
        }
    }

    /// <summary>Finalizes and closes; <paramref name="note"/> non-null means no End record arrived.</summary>
    private static void CloseFile(ref FileStream? stream, ref BinaryWriter? writer, string path,
        uint recordCount, uint lastTimeMs, string? note)
    {
        if (stream == null) return;

        try
        {
            writer?.Flush();
            ReplayFile.WriteFinal(stream, lastTimeMs, recordCount);
            var span = TimeSpan.FromMilliseconds(lastTimeMs);
            var suffix = note == null ? "" : $" ({note})";
            Console.WriteLine($"[Kayıt] Bitti: {Path.GetFileName(path)} · " +
                              $"{(int)span.TotalMinutes:00}:{span.Seconds:00} · {recordCount} kayıt{suffix}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Kayıt] Yazılamadı: {path} — {ex.Message}");
        }
        finally
        {
            AbandonFile(ref stream, ref writer);
        }
    }

    private static void AbandonFile(ref FileStream? stream, ref BinaryWriter? writer)
    {
        try { writer?.Dispose(); } catch (Exception) { /* closing is best effort */ }
        try { stream?.Dispose(); } catch (Exception) { /* closing is best effort */ }
        writer = null;
        stream = null;
    }

    private static void ReportFailure(string path, Exception ex) =>
        Console.WriteLine($"[Kayıt] Yazılamadı: {path} — {ex.Message} (bu maçın kaydı bırakıldı).");

    private string UniquePath(string baseName)
    {
        var path = Path.Combine(_directory, baseName + ReplayFile.EXTENSION);
        for (int i = 2; File.Exists(path) && i < 1000; i++)
            path = Path.Combine(_directory, $"{baseName}_{i}{ReplayFile.EXTENSION}");
        return path;
    }

    /// <summary>Server LOCAL time (§12.2): the operator looks for the match by the clock on the wall.</summary>
    private static string BuildFileName(ReplayMeta meta) =>
        $"{DateTime.Now:yyyy-MM-dd_HH-mm-ss}_{Sanitize(meta.sceneName)}_{Sanitize(meta.modeId)}";

    private static string Sanitize(string? part)
    {
        if (string.IsNullOrWhiteSpace(part)) return "bilinmiyor";
        var sb = new StringBuilder(part.Length);
        foreach (var c in part.Trim())
            sb.Append(Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0 ? '_' : c);
        return sb.ToString();
    }

    /// <summary>Deletes expired recordings (§12.5); <c>replayKeepDays = 0</c> keeps everything.</summary>
    private void PruneOld()
    {
        if (_keepDays <= 0) return;

        try
        {
            if (!System.IO.Directory.Exists(_directory)) return;
            var cutoff = DateTime.Now.AddDays(-_keepDays);
            var deleted = 0;
            foreach (var file in System.IO.Directory.GetFiles(_directory, "*" + ReplayFile.EXTENSION))
            {
                if (File.GetLastWriteTime(file) >= cutoff) continue;
                try
                {
                    File.Delete(file);
                    deleted++;
                }
                catch (Exception) { /* locked/read-only file: the next pass retries */ }
            }

            if (deleted > 0)
                Console.WriteLine($"[Kayıt] {deleted} eski kayıt silindi ({_keepDays} günden eski).");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Kayıt] Eski kayıtlar temizlenemedi: {ex.Message}");
        }
    }

    /// <summary>Empty = <c>replays/</c> next to the exe; a relative path resolves against it too — the
    /// working directory of a shortcut/service is not the server folder.</summary>
    private static string ResolveDirectory(string? configured)
    {
        if (string.IsNullOrWhiteSpace(configured)) return Path.Combine(AppContext.BaseDirectory, "replays");
        var trimmed = configured.Trim();
        return Path.IsPathRooted(trimmed) ? trimmed : Path.Combine(AppContext.BaseDirectory, trimmed);
    }
}
