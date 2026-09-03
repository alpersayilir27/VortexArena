using UnityEngine;
using VortexArena.Core.Arena;

namespace VortexArena.Core.Combat
{
    /// <summary>
    /// Instant blast: FX + sound on every client, damage from the THROWER's client only (one
    /// <c>hit_report</c> per target, Docs/ArenaNet-Protokol.md §10.3 — the protocol has no "area
    /// damage" message).
    /// </summary>
    public sealed class BlastEffect : ThrowableEffect
    {
        /// <summary>Pool nodes built at arm time — one is the blast itself, the second covers a
        /// second bomb landing before the first has faded.</summary>
        private const int PrewarmNodes = 2;

        [Tooltip("Patlama efektinin kaç saniye sonra havuza döneceği.")]
        [SerializeField] private float effectLifetime = 3f;

        /// <summary>Builds the FX pool while the fuse burns, so the explosion itself pays no
        /// <c>Instantiate</c> or shader warm-up.</summary>
        public override void Prewarm(ThrowableDefinition definition)
        {
            if (definition == null)
            {
                return;
            }

            BlastFxPool.Shared.Prewarm(definition.ExplosionPrefab, PrewarmNodes);
        }

        public override void Trigger(Throwable source)
        {
            ThrowableDefinition definition = source != null ? source.Definition : null;
            if (definition == null)
            {
                return;
            }

            Vector3 position = transform.position;
            PlayPresentation(definition, position);

            // ⚠️ Only the thrower reports: remote copies land centimetres apart, so reporting from
            // each would deal the same blast once per client (§6.4).
            if (!source.LocalOwner)
            {
                return;
            }

            ArenaCombat.ReportAreaHit(position, definition.BlastRadius, definition.BlastDamage,
                definition.ThrowableId, definition.EdgeScale, ArenaLayers.AreaTargetMask,
                definition.RequireLineOfSight);

            // The local rig carries no hittable collider, so self damage is a separate pass; the
            // friendly-fire gate is read there, not here.
            ArenaCombat.ReportAreaSelfHit(position, definition.BlastRadius, definition.BlastDamage,
                definition.ThrowableId, definition.EdgeScale, definition.RequireLineOfSight);
        }

        /// <summary>⚠️ Pooled, not instantiated: this object is destroyed the moment the effect fires,
        /// so the presentation cannot live under it — and a per-blast
        /// <c>Instantiate</c>/<c>Destroy</c> pair is a visible hitch on Quest
        /// (<see cref="BlastFxPool"/>).</summary>
        private void PlayPresentation(ThrowableDefinition definition, Vector3 position)
        {
            BlastFxPool.Shared.Play(definition.ExplosionPrefab, position, definition.ExplosionClip,
                definition.ExplosionVolume, effectLifetime);
        }
    }
}
