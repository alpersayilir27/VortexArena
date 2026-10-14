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
        /// The layers an <b>area effect</b> looks for its NETWORK OBJECT targets on: <c>Default</c>
        /// (free-standing network objects) + <see cref="ObstacleName"/> (breakable cover, which sits
        /// on the obstacle layer to be a violation volume too). Players are not found through this
        /// query — <c>ArenaCombat</c> scores them on a body capsule from the avatar list.
        /// <para>
        /// ⚠️ <b>Why not <c>~0</c>:</b> a maskless overlap pulls every grab volume, interaction
        /// trigger, boundary volume and piece of scenery into the query's buffer. A FULL buffer
        /// silently drops real targets — the blast then damages fewer players than it should — so
        /// narrowing the query is a correctness rule, not only a cost one.
        /// </para>
        /// <para>
        /// ⚠️ <b>The invariant this depends on:</b> every network object that can take damage
        /// carries a NON-TRIGGER collider on <c>Default</c> or <see cref="ObstacleName"/>. One moved
        /// to another layer, or turned into a trigger, stops taking blast damage <b>without any
        /// error</b>. Add the layer here when that changes.
        /// </para>
        /// <para>Falls back to <c>Default</c> alone when the obstacle layer is undefined — the same
        /// fail-open shape <see cref="ObstacleMask"/> uses.</para>
        /// </summary>
        public static int AreaTargetMask => DefaultMask | ObstacleMask;

        /// <summary>
        /// The layer a DEAD network body (zombie ragdoll) is moved to. Its contract is one sentence —
        /// <b>"this is scenery, not a target"</b>: it keeps colliding with the world so the body can
        /// settle, but no damage query may find it.
        /// <para>⚠️ The corpse keeps the <c>netId</c> of a living object for the despawn delay
        /// (§10.5). Left on <c>Default</c>, the next round would stop in it and spend a whole burst on
        /// a body that cannot take damage — and the shooter would see no hit anywhere.</para>
        /// </summary>
        public const string ZombieRagdollName = "ZombieRagdoll";

        private static int ragdollMask;
        private static bool ragdollResolved;

        /// <summary>
        /// The mask <b>a hitscan shot queries</b>: everything EXCEPT <see cref="ZombieRagdollName"/>.
        /// <para>Deliberately "everything minus one" and not a target list: remote hit boxes, network
        /// objects and plain geometry all sit on <c>Default</c>, and a positive list would silently drop
        /// the first surface that gets its own layer.</para>
        /// <para>Falls back to <c>~0</c> (the old behaviour) when the layer is undefined — a corpse is
        /// then shootable again, which is a look problem, while a <c>0</c> mask would stop every shot
        /// in the game.</para>
        /// </summary>
        public static int ShotMask => ~ZombieRagdollMask;

        /// <summary>Mask of <see cref="ZombieRagdollName"/>; <c>0</c> when the layer is not defined.</summary>
        public static int ZombieRagdollMask
        {
            get
            {
                if (ragdollResolved)
                {
                    return ragdollMask;
                }

                ragdollResolved = true;

                int layer = LayerMask.NameToLayer(ZombieRagdollName);
                if (layer < 0)
                {
                    // A WARNING, not an error (unlike ObstacleMask): the fallback keeps every shot
                    // working, only corpses stay shootable.
                    Debug.LogWarning(
                        $"[ArenaLayers] '{ZombieRagdollName}' layer'ı projede tanımlı değil — ölü " +
                        "bedenler mermiyi durdurmaya devam edecek. Project Settings > Tags and Layers " +
                        "altında bu adla bir user layer açılmalı.");
                    ragdollMask = 0;
                    return ragdollMask;
                }

                ragdollMask = 1 << layer;
                return ragdollMask;
            }
        }

        /// <summary>Unity's built-in <c>Default</c> layer (index 0) — always defined.</summary>
        private const int DefaultMask = 1;
    }
}
