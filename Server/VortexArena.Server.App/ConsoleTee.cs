using System.Text;

namespace VortexArena.Server.App;

/// <summary>Wraps <c>Console.Out</c> so every printed line also lands in a log file.</summary>
/// <remarks>⚠️ In the field the headsets' own logs arrive here over <c>client_log</c> (§5.1) and the
/// console window scrolls them away; after a session this file is the only place left to look.
/// Never fatal: if the file cannot be opened the server keeps running without one.</remarks>
internal static class ConsoleTee
{
    /// <summary>Redirects <c>Console.Out</c> to console + <paramref name="path"/>.</summary>
    public static void Install(string path)
    {
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var file = new StreamWriter(path, append: true, Encoding.UTF8) { AutoFlush = true };
            Console.SetOut(new TeeWriter(Console.Out, file));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ConsoleTee] günlük dosyası açılamadı ({ex.Message}) — sunucu dosyasız sürüyor.");
        }
    }

    private sealed class TeeWriter : TextWriter
    {
        private readonly TextWriter _console;
        private readonly TextWriter _file;
        private readonly object _gate = new();

        public TeeWriter(TextWriter console, TextWriter file)
        {
            _console = console;
            _file = file;
        }

        public override Encoding Encoding => _console.Encoding;

        // Every Write overload funnels here; connection tasks print concurrently, so the pair of
        // writes must stay atomic or lines interleave mid-word.
        public override void Write(char value)
        {
            lock (_gate)
            {
                _console.Write(value);
                _file.Write(value);
            }
        }

        public override void Write(string? value)
        {
            lock (_gate)
            {
                _console.Write(value);
                _file.Write(value);
            }
        }

        public override void WriteLine(string? value)
        {
            lock (_gate)
            {
                _console.WriteLine(value);
                _file.WriteLine(value);
            }
        }

        public override void Flush()
        {
            lock (_gate)
            {
                _console.Flush();
                _file.Flush();
            }
        }

        protected override void Dispose(bool disposing)
        {
            // The console writer belongs to the runtime — only the file is ours to close.
            if (disposing) _file.Dispose();
            base.Dispose(disposing);
        }
    }
}
