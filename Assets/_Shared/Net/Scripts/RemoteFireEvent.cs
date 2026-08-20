using UnityEngine;

namespace VortexArena.Net
{
    /// <summary>
    /// A single remote shot/throw event relayed by the server (the Unity-side counterpart of a 0x04
    /// EventBatch entry, §6.5). UdpStateChannel unpacks it on the network thread and publishes it on the
    /// MAIN thread through NetEvents.OnRemoteFireEvent; the presentation side (Core's remote shot
    /// effect) listens.
    /// <para>⚠️ Our own events NEVER arrive here — the channel filters by <c>playerId</c> (the shooter
    /// ignores its own event, as with its own pose in the snapshot, §6.5).</para>
    /// </summary>
    public struct RemoteFireEvent
    {
        /// <summary>The remote player that produced the event.</summary>
        public int playerId;

        /// <summary><c>FireEventEntry.KIND_SHOT</c> (hitscan) or <c>KIND_THROW</c> (throw).</summary>
        public byte kind;

        /// <summary>Which hand the event came from: true = right.</summary>
        public bool rightHand;

        /// <summary>
        /// The item's <c>netItemId</c> at the moment of the event (§6.6); <b>0 = unresolved</b> (an
        /// empty hand was reported or the byte was lost). It resolves the presentation profile, so the
        /// event is self-sufficient even if the state bytes (snapshot <c>itemL</c>/<c>itemR</c>) are
        /// lost.
        /// </summary>
        public byte itemId;

        /// <summary>
        /// Unit aim direction, in <b>ARENA space</b>. Converting it to world space is the drawing
        /// side's job (the Net layer does not know the arena↔world transform).
        /// </summary>
        public Vector3 arenaDirection;

        /// <summary>
        /// ⚠️ <b>Meaning depends on <see cref="kind"/>:</b> <c>KIND_SHOT</c> = hit <b>distance</b>
        /// (m, where the tracer ends), <c>KIND_THROW</c> = initial <b>speed</b> (m/s). Both ride the
        /// same u16 wire field (cm / cm-per-s), hence one name — a consumer must check the kind.
        /// </summary>
        public float magnitude;

        /// <summary>
        /// The server tick the event was produced on.
        /// <para><b>Why carried:</b> the event must play on its own tick, on the receiver's
        /// <b>interpolation clock</b> — the hand pose is drawn <c>INTERP_DELAY_MS</c> behind, so the
        /// tracer starts on that tick, not "now". Hence 20 Hz batching adds NO perceived latency: a
        /// ≤50 ms batch wait dissolves inside the 100 ms interp buffer (§6.5).</para>
        /// </summary>
        public uint serverTick;
    }
}
