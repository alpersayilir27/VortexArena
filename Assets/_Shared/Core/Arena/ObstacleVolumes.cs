using System.Collections.Generic;
using UnityEngine;

namespace VortexArena.Core.Arena
{
    /// <summary>The single answer to "is this point inside an <b>inner obstacle</b>"
    /// (the <see cref="ArenaLayers.ObstacleName"/> layer, Docs/ArenaNet-Protokol.md §10.9).</summary>
    /// <remarks>
    /// Three systems ask the same question — <c>ObstacleViolationProbe</c> (head/hand → penalty +
    /// fire gate), <c>ArenaCombat.IsMuzzleBlocked</c> and <c>ArenaCombat.IsWeaponBlocked</c>
    /// (<see cref="OverlapsBox"/>). Copying the layer mask, the convexity filter and the
    /// "ClosestPoint returns the point itself when inside" fact per caller would make one of them
    /// drift, with "obstacles sometimes do not work" as the symptom.
    /// <para>⚠️ An obstacle collider must be CONVEX (Box/Sphere/Capsule or <c>MeshCollider</c> +
    /// <c>Convex</c>): on a non-convex <c>MeshCollider</c> <see cref="Collider.ClosestPoint"/>
    /// returns the input point unchanged → every point reads "inside" → everyone in the scene starts
    /// dying instantly. Such a collider is permanently ignored here (loud failure: one error line, no
    /// penalty) and also scanned for in the editor (<c>Engel Hacimlerini Denetle</c>).</para>
    /// <para>Two usage shapes with SEPARATE buffers: <see cref="Sample"/> +
    /// <see cref="Contains(Vector3,int)"/> queries once and tests MANY points (body measurement: one
    /// physics query, 20+ points), its cache belonging to the last <see cref="Sample"/> caller;
    /// <see cref="ContainsPoint"/> is one-shot and never touches that cache, so the shot path cannot
    /// spoil a body-measurement pass.</para>
    /// </remarks>
    public static class ObstacleVolumes
    {
        /// <summary>Candidate cap; the excess is ignored — more than eight obstacles around one
        /// player at once is a scene setup error.</summary>
        public const int MaxCandidates = 8;

        /// <summary>Radius of the single-point query (m). A zero-radius sphere reports no overlap on
        /// some drivers; the decisive answer comes from <see cref="Collider.ClosestPoint"/> anyway,
        /// this radius only gathers candidates.</summary>
        private const float PointQueryRadius = 0.01f;

        private static readonly Collider[] Candidates = new Collider[MaxCandidates];
        private static readonly Bounds[] CandidateBounds = new Bounds[MaxCandidates];
        private static readonly Collider[] PointCandidates = new Collider[MaxCandidates];
        private static readonly Collider[] BoxCandidates = new Collider[MaxCandidates];
        private static readonly Collider[] ClearanceCandidates = new Collider[MaxCandidates];

        /// <summary>Colliders rejected for not being convex — so the warning is logged once.</summary>
        private static readonly HashSet<int> Rejected = new HashSet<int>();

        /// <summary>The last collider that said "inside" — DIAGNOSTICS only. Do not write rules
        /// against it: the answer is the return of <see cref="Contains(Vector3,int)"/>.</summary>
        public static Collider LastHit { get; private set; }

        /// <summary>Collects obstacle candidates inside the given sphere and reads their bounds once
        /// per pass (<see cref="Collider.bounds"/> goes native on every access). Returns the
        /// candidate count to pass to <see cref="Contains(Vector3,int)"/>.</summary>
        public static int Sample(Vector3 center, float radius)
        {
            int mask = ArenaLayers.ObstacleMask;
            if (mask == 0)
            {
                return 0; // layer undefined — ArenaLayers already shouted once
            }

            int count = Physics.OverlapSphereNonAlloc(center, radius, Candidates, mask,
                QueryTriggerInteraction.Ignore);
            if (count > MaxCandidates)
            {
                count = MaxCandidates;
            }

            int kept = 0;
            for (int i = 0; i < count; i++)
            {
                Collider collider = Candidates[i];
                if (collider == null || !IsUsable(collider))
                {
                    continue;
                }

                Candidates[kept] = collider;
                CandidateBounds[kept] = collider.bounds;
                kept++;
            }

            return kept;
        }

        /// <summary>Is the point inside ANY of the candidates <see cref="Sample"/> gathered (union
        /// semantics): otherwise a head at the seam of two boxes would escape as "not fully inside
        /// either".</summary>
        public static bool Contains(Vector3 point, int count)
        {
            for (int i = 0; i < count; i++)
            {
                if (!CandidateBounds[i].Contains(point))
                {
                    continue; // cheap AABB reject — most points return here
                }

                Collider collider = Candidates[i];
                if (collider == null || !IsPointInside(collider, point))
                {
                    continue;
                }

                LastHit = collider;
                return true;
            }

            return false;
        }

        /// <summary>Query + test for a single point (own buffer). Used by the shot path: one physics
        /// query per shot, ten per second at 600 RPM.</summary>
        public static bool ContainsPoint(Vector3 point)
        {
            int mask = ArenaLayers.ObstacleMask;
            if (mask == 0)
            {
                return false;
            }

            int count = Physics.OverlapSphereNonAlloc(point, PointQueryRadius, PointCandidates, mask,
                QueryTriggerInteraction.Ignore);
            if (count > MaxCandidates)
            {
                count = MaxCandidates;
            }

            for (int i = 0; i < count; i++)
            {
                Collider collider = PointCandidates[i];
                if (collider == null || !IsUsable(collider) || !IsPointInside(collider, point))
                {
                    continue;
                }

                LastHit = collider;
                return true;
            }

            return false;
        }

        /// <summary>Distance from the point to the nearest obstacle SURFACE (m), capped at
        /// <paramref name="maxDistance"/>; <c>0</c> when the point is inside an obstacle.</summary>
        /// <remarks>
        /// Why a separate question: <see cref="Contains(Vector3,int)"/> answers "inside", which is
        /// too late for closing the VIEW — the camera's near plane sits a few centimeters in FRONT
        /// of the eye, so geometry starts clipping while the eye is still outside and the inside of
        /// the solid becomes readable. Deciding before clipping needs the real surface distance
        /// (consumer: <c>ObstacleViolationProbe</c>'s fade gate).
        /// <para>⚠️ Only meaningful from outside: <see cref="Collider.ClosestPoint"/> returns the
        /// point itself when inside, so the result inside is inevitably 0 — which is the wanted
        /// answer, inside is already the closest state.</para>
        /// <para>⚠️ Does NOT write <see cref="LastHit"/>: that field is the "last collider that said
        /// inside" diagnostic, and a per-frame proximity measurement would keep overwriting it.</para>
        /// <para>Has its own buffer — never touches <see cref="Sample"/>'s cache.</para>
        /// </remarks>
        public static float DistanceToSurface(Vector3 point, float maxDistance)
        {
            int mask = ArenaLayers.ObstacleMask;
            if (mask == 0)
            {
                return maxDistance; // layer undefined — ArenaLayers already shouted once
            }

            int count = Physics.OverlapSphereNonAlloc(point, maxDistance, ClearanceCandidates, mask,
                QueryTriggerInteraction.Ignore);
            if (count > MaxCandidates)
            {
                count = MaxCandidates;
            }

            float nearest = maxDistance;
            for (int i = 0; i < count; i++)
            {
                Collider collider = ClearanceCandidates[i];
                if (collider == null || !IsUsable(collider))
                {
                    continue;
                }

                float distance = Vector3.Distance(collider.ClosestPoint(point), point);
                if (distance < nearest)
                {
                    nearest = distance;
                    if (nearest <= 0f)
                    {
                        break; // inside — nothing can be closer
                    }
                }
            }

            return nearest;
        }

        /// <summary>Does an oriented BOX intersect any obstacle. Its only consumer is the weapon body
        /// test: "is the muzzle inside" is a point question, "does any part of the weapon touch" is a
        /// VOLUME question that point sampling cannot answer (a weapon touching a wall with its stock
        /// may have no sample point inside).
        /// <para>⚠️ Unlike <see cref="Contains(Vector3,int)"/> this also answers correctly for
        /// non-convex colliders: box-mesh intersection does not rely on <c>ClosestPoint</c>, so that
        /// API's "every point is inside" lie does not reach here. Hence no convexity filter — the
        /// layer must be convex as a rule, but a collider that is not gets a correct answer here
        /// instead of a silently wrong one.</para></summary>
        /// <param name="center">WORLD center of the box.</param>
        /// <param name="halfExtents">Half extents of the box (world units).</param>
        /// <param name="rotation">World rotation of the box.</param>
        public static bool OverlapsBox(Vector3 center, Vector3 halfExtents, Quaternion rotation)
        {
            int mask = ArenaLayers.ObstacleMask;
            if (mask == 0)
            {
                return false;
            }

            int count = Physics.OverlapBoxNonAlloc(center, halfExtents, BoxCandidates, rotation, mask,
                QueryTriggerInteraction.Ignore);

            for (int i = 0; i < count && i < MaxCandidates; i++)
            {
                if (BoxCandidates[i] == null)
                {
                    continue;
                }

                LastHit = BoxCandidates[i];
                return true;
            }

            return false;
        }

        /// <summary>On a convex collider <see cref="Collider.ClosestPoint"/> returns the point ITSELF
        /// when it is inside. ⚠️ The inverse is not measurable: surface distance from inside cannot be
        /// obtained through this API, so no depth computation can be written — depth is only
        /// approximated by sampling MULTIPLE points (as the head sphere does).</summary>
        private static bool IsPointInside(Collider collider, Vector3 point) =>
            (collider.ClosestPoint(point) - point).sqrMagnitude <= 1e-8f;

        /// <summary>⚠️ A non-convex <see cref="MeshCollider"/> is UNUSABLE (rationale in the class
        /// summary). Such a collider is permanently ignored and reported once — loud failure rather
        /// than a silent massacre.</summary>
        private static bool IsUsable(Collider collider)
        {
            if (collider is not MeshCollider mesh || mesh.convex)
            {
                return true;
            }

            if (Rejected.Add(mesh.GetInstanceID()))
            {
                Debug.LogError(
                    $"[ObstacleVolumes] '{mesh.name}' objesi '{ArenaLayers.ObstacleName}' layer'ında " +
                    "ama collider'ı KONVEKS DEĞİL (MeshCollider + Convex kapalı). Bu obje engel " +
                    "hesabından ÇIKARILDI — nokta-içeride testi non-convex mesh'te her zaman " +
                    "'içeride' der ve tüm oyuncuları anında öldürürdü. Convex işaretle ya da kaba bir " +
                    "Box/Capsule collider kullan.", mesh);
            }

            return false;
        }
    }
}
