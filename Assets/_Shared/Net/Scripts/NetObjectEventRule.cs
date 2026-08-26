using System;
using UnityEngine;
using VortexArena.Protocol;

namespace VortexArena.Net
{
    /// <summary>One allowed object event of a kind (§10.10): the wire <c>name</c> plus the two gates the
    /// server runs on it. It lives in its own file because it is a serialized secondary type.
    /// <para>⚠️ <c>name</c> is free text on the wire and NOT free text on the server — a name missing
    /// from this list is rejected. The list is exported into <c>maps.json</c> (<c>kinds[].events[]</c>),
    /// so a typo here is not a compile error, it is an interaction that silently never happens.</para></summary>
    [Serializable]
    public sealed class NetObjectEventRule
    {
        [Tooltip("Telde taşınan olay adı (maps.json kinds[].events[] ile BİREBİR aynı).")]
        [SerializeField] private string name = "";

        [Tooltip("Kimler tetikleyebilir: herkes / yalnız objenin sahibi.")]
        [SerializeField] private NetObjectEventPolicy policy = NetObjectEventPolicy.Anyone;

        [Tooltip("Hangi fazda kabul edilir: yalnız playing / her faz (lobi dahil).")]
        [SerializeField] private NetObjectPhaseGate phaseGate = NetObjectPhaseGate.Playing;

        public string Name => name;

        public NetObjectEventPolicy Policy => policy;

        public NetObjectPhaseGate PhaseGate => phaseGate;

        /// <summary>Wire value of <see cref="Policy"/> (<c>ArenaProtocol.OBJECT_EVENT_POLICY_*</c>).</summary>
        public string WirePolicy => policy == NetObjectEventPolicy.Owner
            ? ArenaProtocol.OBJECT_EVENT_POLICY_OWNER
            : ArenaProtocol.OBJECT_EVENT_POLICY_ANYONE;

        /// <summary>Wire value of <see cref="PhaseGate"/> (<c>ArenaProtocol.OBJECT_PHASE_GATE_*</c>).</summary>
        public string WirePhaseGate => phaseGate == NetObjectPhaseGate.Any
            ? ArenaProtocol.OBJECT_PHASE_GATE_ANY
            : ArenaProtocol.OBJECT_PHASE_GATE_PLAYING;
    }
}
