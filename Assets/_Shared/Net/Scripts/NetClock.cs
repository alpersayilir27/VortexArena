using System;

namespace VortexArena.Net
{
    /// <summary>
    /// The clock every network "now" is read from: receive stamps, staleness and interpolation.
    /// <para>Live it IS <c>Environment.TickCount</c> (same value, same wrap behaviour — int
    /// subtraction everywhere). Replay installs an override so the registries interpolate on the
    /// playback clock instead of wall time (<c>Docs/ArenaNet-Protokol.md</c> §12.4).</para>
    /// <para>⚠️ Outgoing <c>clientTimeMs</c> is NOT read from here: it is a real send stamp the
    /// server measures against, and replay sends nothing.</para>
    /// </summary>
    public static class NetClock
    {
        // Read from the net threads, written from the main thread → volatile reference.
        private static volatile Func<int> _source;

        /// <summary>Milliseconds on a monotonic axis; only differences are meaningful.</summary>
        public static int NowMs
        {
            get
            {
                Func<int> source = _source;
                return source != null ? source() : Environment.TickCount;
            }
        }

        /// <summary>Is a non-live clock installed (replay)?</summary>
        public static bool HasOverride => _source != null;

        public static void SetOverride(Func<int> source)
        {
            _source = source;
        }

        public static void ClearOverride()
        {
            _source = null;
        }
    }
}
