using UnityEngine;
using UnityEngine.Events;

namespace VortexArena.Core.Arena
{
    /// <summary>
    /// <b>Base zone</b>: the strip along one edge of the arena (red/blue). A dead player revives by
    /// physically walking here — in <see cref="ModeReviveAnchor.OwnBase"/> mode
    /// <c>PlayerCombatState</c> reads entry into this zone as the precondition for
    /// <c>revive_request</c> (Docs/ArenaNet-Protokol.md §10.4).
    /// <para>
    /// <b>The detection area IS THE DRAWN STRIP</b>: the rectangle covered by the Renderers under
    /// the zone is measured in the zone's LOCAL space and that is the boundary. There is NO
    /// hand-entered size field and none is added — scaling/rotating/moving the zone = scaling/
    /// rotating/moving the strip. If there were a numeric field it would silently drift from the
    /// visual: the player would appear to stand on the red strip yet fail to revive (or revive while
    /// standing next to it) and the error would show up nowhere.
    /// </para>
    /// <para>
    /// ⚠️ <b>Height is ignored</b> (XZ only): the player's HMD is meters above the strip and cannot
    /// be compared with the strip's own thickness. The measurement is taken <b>once</b>, in
    /// <see cref="Awake"/> — the strip is static in the scene; disabled (hidden) Renderers count
    /// too, because <see cref="BaseZoneVisibility"/> hides the strip in team-less modes.
    /// </para>
    /// <para>
    /// It tracks the HMD position in the zone's LOCAL space (no physics, the same pattern as
    /// <see cref="ArenaBoundary"/>) and raises events when the player enters and leaves; as the game
    /// grows, behaviours like weapon refill / healing can hook onto those events.
    /// </para>
    /// <para>
    /// <see cref="team"/> = who can use this zone. A zone marked <see cref="Team.Neutral"/> is used
    /// by EVERYONE; in a team-less mode (§10.5) the player uses all zones. Several zones belonging
    /// to the same team can be placed — entering any one of them is enough.
    /// </para>
    /// <para>
    /// ⚠️ The zone does NOT TRIGGER a position change: the player walks physically, they are not
    /// teleported.
    /// </para>
    /// </summary>
    public class BaseZone : MonoBehaviour
    {
        [Tooltip("Bu taban bölgesini kimler kullanabilir. Neutral = herkes.")]
        [SerializeField] private Team team = Team.Red;

        [Header("References")]
        [Tooltip("HMD transform (CenterEyeAnchor). Falls back to Camera.main.")]
        [SerializeField] private Transform head;

        public UnityEvent onPlayerEntered;
        public UnityEvent onPlayerExited;

        /// <summary>Who can use this zone; <see cref="Team.Neutral"/> = everyone.</summary>
        public Team Team => team;

        /// <summary>Is the local player's HMD inside the zone (FROZEN while the component is disabled).</summary>
        public bool IsPlayerInside { get; private set; }

        /// <summary>Corners of the rectangle measured from the strip, in the zone's LOCAL space
        /// (x, z). The strip may be offset from the zone's pivot — which is why no center is
        /// assumed.</summary>
        private Vector2 _areaMin;
        private Vector2 _areaMax;

        private void Awake()
        {
            if (TryMeasureStrip(out _areaMin, out _areaMax))
            {
                return;
            }

            // Loud failure: a zone without measurements would silently become a zone that can never
            // be entered, and the player would fail to revive while standing on the strip. Once the
            // component is disabled, PlayerCombatState reads this as "no open base" and its own
            // fail-open kicks in (§10.4).
            Debug.LogError(
                $"BaseZone ({name}): altında Renderer yok — algılama alanı ÇİZİLEN ŞERİTTEN " +
                "türer, ölçü alınamadı. Bölge kapatıldı; şerit mesh'ini bölgenin altına koy.", this);
            enabled = false;
        }

        private void Update()
        {
            if (!ResolveHead())
                return;

            Vector3 local = transform.InverseTransformPoint(head.position);
            bool inside = local.x >= _areaMin.x && local.x <= _areaMax.x &&
                          local.z >= _areaMin.y && local.z <= _areaMax.y;
            if (inside == IsPlayerInside)
                return;

            IsPlayerInside = inside;
            Debug.Log($"BaseZone: player {(inside ? "entered" : "left")} {team} base.");
            if (inside)
                onPlayerEntered?.Invoke();
            else
                onPlayerExited?.Invoke();
        }

        /// <summary>
        /// Measures the rectangle covered by the strip in the zone's local XZ.
        /// <para>
        /// ⚠️ <c>Renderer.bounds</c> (a world-axis AABB) is NOT USED: on a rotated strip the box
        /// inflates and the zone spills outside the strip. The measurement is taken by moving the
        /// eight corners of each Renderer's <b>own</b> local box into world space and from there
        /// into the zone's local space — a rotated strip, and a strip skewed relative to the zone,
        /// both come out correct.
        /// </para>
        /// </summary>
        private bool TryMeasureStrip(out Vector2 min, out Vector2 max)
        {
            min = Vector2.zero;
            max = Vector2.zero;

            // Disabled ones included: in a team-less mode BaseZoneVisibility hides the strip, and
            // this component may wake up after it during scene load.
            Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
            bool measured = false;

            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null)
                {
                    continue;
                }

                Bounds local = renderer.localBounds;
                Vector3 center = local.center;
                Vector3 extents = local.extents;
                Transform space = renderer.transform;

                for (int corner = 0; corner < 8; corner++)
                {
                    var offset = new Vector3(
                        (corner & 1) == 0 ? -extents.x : extents.x,
                        (corner & 2) == 0 ? -extents.y : extents.y,
                        (corner & 4) == 0 ? -extents.z : extents.z);

                    Vector3 point = transform.InverseTransformPoint(space.TransformPoint(center + offset));

                    if (!measured)
                    {
                        min = new Vector2(point.x, point.z);
                        max = min;
                        measured = true;
                        continue;
                    }

                    min.x = Mathf.Min(min.x, point.x);
                    min.y = Mathf.Min(min.y, point.z);
                    max.x = Mathf.Max(max.x, point.x);
                    max.y = Mathf.Max(max.y, point.z);
                }
            }

            return measured;
        }

        /// <summary>
        /// The HMD transform; retried EVERY FRAME until found.
        /// <para>
        /// ⚠️ <b>Resolving once (previously in <c>Awake</c>) is not enough and dies silently:</b>
        /// <see cref="Camera.main"/> only returns something once an <b>enabled</b> camera tagged
        /// <c>MainCamera</c> has registered; if the rig's <c>CenterEyeAnchor</c> camera registers
        /// AFTER this component's <c>Awake</c>, the field stays <c>null</c> permanently. In that
        /// case <see cref="IsPlayerInside"/> is frozen at <c>false</c> for its whole lifetime while
        /// <c>PlayerCombatState.HasOpenBaseZone</c> stays <b>true</b> (the component is enabled) —
        /// so the "no zone" fail-open does not kick in either: the player standing right on the
        /// strip can neither revive nor be counted as ready during round gathering.
        /// </para>
        /// <para>The same pattern is used in <c>PlayerCombatState.ResolveHead</c>. If the field is
        /// filled in the Inspector nothing is searched — it can be wired by hand, as in
        /// <c>ArenaBoundary</c>.</para>
        /// </summary>
        private bool ResolveHead()
        {
            if (head != null)
                return true;

            Camera cam = Camera.main;
            if (cam == null)
                return false;

            head = cam.transform;
            return true;
        }

#if UNITY_EDITOR
        /// <summary>Draws the detection rectangle while selected — since the measurement now comes
        /// from the visual, the only way to inspect it is by eye. It is fresh without waiting for
        /// Play: it is re-measured on every draw.
        /// </summary>
        private void OnDrawGizmosSelected()
        {
            if (!TryMeasureStrip(out Vector2 min, out Vector2 max))
            {
                return;
            }

            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = team == Team.Blue ? Color.cyan : team == Team.Red ? Color.red : Color.white;

            var a = new Vector3(min.x, 0f, min.y);
            var b = new Vector3(max.x, 0f, min.y);
            var c = new Vector3(max.x, 0f, max.y);
            var d = new Vector3(min.x, 0f, max.y);

            Gizmos.DrawLine(a, b);
            Gizmos.DrawLine(b, c);
            Gizmos.DrawLine(c, d);
            Gizmos.DrawLine(d, a);
        }
#endif
    }
}
