using UnityEngine;

namespace VortexArena.Core.Combat
{
    /// <summary>
    /// Instant blast: FX + sound on every client, damage from the THROWER's client only (one
    /// <c>hit_report</c> per target, Docs/ArenaNet-Protokol.md §10.3 — the protocol has no "area
    /// damage" message).
    /// </summary>
    public sealed class BlastEffect : ThrowableEffect
    {
        [Tooltip("Patlama efekti prefabının kaç saniye sonra silineceği.")]
        [SerializeField] private float effectLifetime = 3f;

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
                definition.ThrowableId, definition.EdgeScale, ~0, definition.RequireLineOfSight);

            // The local rig carries no hittable collider, so self damage is a separate pass; the
            // friendly-fire gate is read there, not here.
            ArenaCombat.ReportAreaSelfHit(position, definition.BlastRadius, definition.BlastDamage,
                definition.ThrowableId, definition.EdgeScale, definition.RequireLineOfSight);
        }

        private void PlayPresentation(ThrowableDefinition definition, Vector3 position)
        {
            if (definition.ExplosionPrefab != null)
            {
                // Detached from the throwable: this object is destroyed the moment the effect fires.
                GameObject fx = Instantiate(definition.ExplosionPrefab, position, Quaternion.identity);
                Destroy(fx, Mathf.Max(0.1f, effectLifetime));
            }

            if (definition.ExplosionClip != null)
            {
                AudioSource.PlayClipAtPoint(definition.ExplosionClip, position, definition.ExplosionVolume);
            }
        }
    }
}
