using UnityEngine;

namespace VortexArena.Core.Arena
{
    /// <summary>
    /// The layer contract of the arena geometry — <b>the single place the layer name is written</b>.
    /// <para>
    /// <b>Why a class:</b> a layer name is a string and <see cref="LayerMask.NameToLayer"/> returns
    /// <c>-1</c> for an undefined name (the mask becomes <c>0</c> and the query silently finds
    /// nothing). If the name were written in three separate files, the symptom of a typo would be
    /// "the system does not work at all" and no error would show up anywhere. Here it is resolved
    /// once and, if not found, shouts explicitly <b>once</b>.
    /// </para>
    /// </summary>
    public static class ArenaLayers
    {
        /// <summary>
        /// The <b>inner obstacle</b> layer: pillar, crate, chest, block. Its contract is a single
        /// sentence — <b>"being inside this is a violation"</b> (Docs/ArenaNet-Protokol.md §10.9).
        /// <para>
        /// ⚠️ <b>The arena's OUTER walls, floor and ceiling are NOT put on this layer.</b> The outer
        /// boundary is managed by <see cref="ArenaBoundary"/> (fade + warning, no damage). The
        /// rationale is calibration: since the outer wall is right next to the player at all times,
        /// a drifting alignment would constantly produce false violations and the player would die
        /// for no reason.
        /// </para>
        /// <para>
        /// ⚠️ A collider on this layer <b>must be CONVEX</b> (Box/Sphere/Capsule or
        /// <c>MeshCollider</c> + <c>Convex</c>) — rationale in
        /// <see cref="VortexArena.Core.Arena.ObstacleVolumes"/>.
        /// </para>
        /// </summary>
        public const string ObstacleName = "Obstacle";

        private static int obstacleMask;
        private static bool obstacleResolved;

        /// <summary>
        /// The mask of the <see cref="ObstacleName"/> layer; <c>0</c> when the layer is not defined
        /// (no query matches → the system is disabled <b>with an error line</b>, not silently).
        /// </summary>
        public static int ObstacleMask
        {
            get
            {
                if (obstacleResolved)
                {
                    return obstacleMask;
                }

                obstacleResolved = true;

                int layer = LayerMask.NameToLayer(ObstacleName);
                if (layer < 0)
                {
                    // ⚠️ An ERROR, not a warning: without this the obstacle violation system does
                    // not work at all and the symptom is "nothing happens" — an undiagnosable
                    // silence.
                    Debug.LogError(
                        $"[ArenaLayers] '{ObstacleName}' layer'ı projede tanımlı değil — engel ihlali " +
                        "tespiti DEVRE DIŞI. Project Settings > Tags and Layers altında bu adla bir " +
                        "user layer açılmalı.");
                    obstacleMask = 0;
                    return obstacleMask;
                }

                obstacleMask = 1 << layer;
                return obstacleMask;
            }
        }

        /// <summary>
        /// The layers an <b>area effect</b> looks for its targets on: <c>Default</c> (player hit
        /// boxes and free-standing network objects) + <see cref="ObstacleName"/> (breakable cover,
        /// which sits on the obstacle layer to be a violation volume too).
        /// <para>
        /// ⚠️ <b>Why not <c>~0</c>:</b> a maskless overlap pulls every grab volume, interaction
        /// trigger, boundary volume and piece of scenery into the query's buffer. A FULL buffer
        /// silently drops real targets — the blast then damages fewer players than it should — so
        /// narrowing the query is a correctness rule, not only a cost one.
        /// </para>
        /// <para>
        /// ⚠️ <b>The invariant this depends on:</b> anything that can take damage carries a
        /// NON-TRIGGER collider on <c>Default</c> or <see cref="ObstacleName"/>. A hit box moved to
        /// another layer, or turned into a trigger, stops taking blast damage <b>without any
        /// error</b>. Add the layer here when that changes.
        /// </para>
        /// <para>Falls back to <c>Default</c> alone when the obstacle layer is undefined — the same
        /// fail-open shape <see cref="ObstacleMask"/> uses.</para>
        /// </summary>
        public static int AreaTargetMask => DefaultMask | ObstacleMask;

        /// <summary>Unity's built-in <c>Default</c> layer (index 0) — always defined.</summary>
        private const int DefaultMask = 1;
    }
}
