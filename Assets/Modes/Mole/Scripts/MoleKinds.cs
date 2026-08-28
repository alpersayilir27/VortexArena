namespace VortexArena.Modes.Mole
{
    /// <summary>The client's ONE vocabulary for the Mole mode: kind name, event name, stage numbers and
    /// payload keys.
    /// <para>⚠️ Mirrors the <c>mole</c> table of <c>Docs/ArenaNet-Protokol.md</c> §10.5 one to one. A
    /// literal typed at a call site instead of a constant here is silently rejected by the server
    /// (<c>kinds[].events[]</c> validation) and looks like "the hammer does nothing".</para></summary>
    internal static class MoleKinds
    {
        // ------------------------------------------------------------------- kind + event

        public const string Hole = "mole_hole";

        public const string EventWhack = "whack";

        // ------------------------------------------------------------------- stages

        public const int StageHidden = 0;
        public const int StageUp = 1;
        public const int StageSquashed = 2;

        // ------------------------------------------------------------------- payload keys (§10.10 `s`)

        /// <summary>Pop counter — the nonce a <c>whack</c> carries back.</summary>
        public const string PayloadNonce = "n";

        /// <summary>Mole colour: <see cref="ColorRed"/> / <see cref="ColorBlue"/>.</summary>
        public const string PayloadColor = "c";

        /// <summary>Who smashed it (only in <see cref="StageSquashed"/>).</summary>
        public const string PayloadBy = "by";

        /// <summary>Was it the right colour: <c>1</c> / <c>0</c> (only in <see cref="StageSquashed"/>).</summary>
        public const string PayloadOk = "ok";

        public const string ColorRed = "red";
        public const string ColorBlue = "blue";

        // ------------------------------------------------------------------- modeState (§10.5)

        /// <summary>Per-player counter token prefix: <c>p&lt;playerId&gt;:&lt;doğru&gt;/&lt;yanlış&gt;</c>.</summary>
        public const string ModeStatePlayerPrefix = "p";
    }
}
