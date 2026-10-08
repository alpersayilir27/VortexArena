namespace VortexArena.Net
{
    /// <summary>
    /// Replay playback flag for the Net layer: while it is on NOTHING connects, discovers or sends
    /// (§12.4). The App layer owns the decision (<c>AppSession.ReplayPath</c>) and writes it here —
    /// Net cannot read App.
    /// </summary>
    public static class ReplayMode
    {
        /// <summary>True while this process plays a recording instead of talking to a server.</summary>
        public static bool Active;

        /// <summary>True only while a seek fast-feeds records in one frame; one-shot cues must stay
        /// silent then, or every kill line of the skipped stretch queues up.</summary>
        public static bool Seeking;
    }
}
