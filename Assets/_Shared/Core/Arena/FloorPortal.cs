using System.Collections.Generic;
using UnityEngine;
using VortexArena.Core.Combat;
using VortexArena.Core.UI;
using VortexArena.Net;

namespace VortexArena.Core.Arena
{
    /// <summary>A floor-to-floor hop point: two stacked discs, standing still on one for
    /// <see cref="DwellSeconds"/> takes the player to the other floor.</summary>
    /// <remarks>
    /// <b>The portal IS the level definition:</b> <see cref="ArenaFloors"/> derives the arena's floor
    /// heights from the portals placed in the scene, so no height is typed anywhere twice
    /// (<see cref="BaseZone"/>'s philosophy — the drawn thing is the data).
    /// <para>⚠️ <b>Physically only ONE player fits on a disc</b>, so the lowest player id inside it
    /// owns it and everyone else is told "meşgul". Without this two players standing on the same
    /// physical spot would both hop and then collide on the other floor.</para>
    /// <para>⚠️ After a hop the player ARRIVES on the paired disc; the portal stays disarmed until
    /// they step off it, otherwise the dwell would immediately send them back.</para>
    /// <para>Dead players may use portals: the death fade already moves them to their base floor and
    /// blocking the hop would strand a corpse walking back.</para>
    /// </remarks>
    public class FloorPortal : MonoBehaviour
    {
        /// <summary>Disc radius (m) — a 50 cm disc: one person, not two.</summary>
        public const float RadiusMeters = 0.25f;

        /// <summary>Uninterrupted standing time that triggers the hop (s).</summary>
        private const float DwellSeconds = 2f;

        /// <summary>Minimum interval between rig searches when none is found (s).</summary>
        private const float RigSearchIntervalSeconds = 0.5f;

        /// <summary>Minimum interval between HUD searches when none is found (s).</summary>
        private const float HudSearchIntervalSeconds = 0.5f;

        private static readonly Color IdleColor = new Color(0.35f, 0.85f, 1f, 0.45f);
        private static readonly Color ChargedColor = Color.white;
        private static readonly Color BusyColor = new Color(1f, 0.25f, 0.2f, 0.6f);
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        // Occupancy query buffer: shared, the query is single-threaded and per-frame.
        private static readonly List<int> IdBuffer = new List<int>();

        [Tooltip("Üst diskin alt diske göre yüksekliği (m). Kat yüksekliği BUNDAN türer — " +
                 "kat listesine elle yazılan bir sayı yoktur.")]
        [SerializeField, Min(0.5f)] private float upperHeight = 3f;

        [Header("Audio (boş = sessiz)")]
        [Tooltip("Diskte beklerken döngüyle çalar.")]
        [SerializeField] private AudioSource chargeSound;
        [Tooltip("Geçiş anında bir kez çalar.")]
        [SerializeField] private AudioSource teleportSound;
        [Tooltip("Portal başka oyuncudayken bir kez çalar.")]
        [SerializeField] private AudioSource busySound;

        // Wired by the prefab builder, never by hand: a mis-wired disc would silently move the hop
        // target away from the drawn one.
        [SerializeField, HideInInspector] private Transform lowerDisc;
        [SerializeField, HideInInspector] private Transform upperDisc;
        [SerializeField, HideInInspector] private Transform lowerFill;
        [SerializeField, HideInInspector] private Transform upperFill;

        private MaterialPropertyBlock _block;
        private ModeHudBase _hud;
        private float _hudSearchTime;
        private OVRCameraRig _rig;
        private float _rigSearchTime;

        private int _floorVersion = -1;
        private int _lowerFloor;
        private float _dwell;
        private bool _armed = true;
        private bool _busyAnnounced;
        private bool _noticeShown;
        private bool _idleVisuals;

        /// <summary>Height of the upper disc above the lower one (m).</summary>
        public float UpperHeight => upperHeight;

        /// <summary>Floor the lower disc stands on.</summary>
        public int LowerFloor
        {
            get
            {
                EnsureFloor();
                return _lowerFloor;
            }
        }

        /// <summary>Floor the upper disc stands on.</summary>
        public int UpperFloor => LowerFloor + 1;

        public Transform LowerDisc => lowerDisc;
        public Transform UpperDisc => upperDisc;

        private void OnEnable()
        {
            ArenaFloors.MarkDirty();
            FloorState.Changed += HandleFloorChanged;
            _floorVersion = -1;
            _hud = null;
            _armed = true;
            ResetDwell();
        }

        private void OnDisable()
        {
            ArenaFloors.MarkDirty();
            FloorState.Changed -= HandleFloorChanged;
            ResetDwell();
        }

        /// <summary>ANY floor change (this portal's, another portal's, the death drop, a server reset)
        /// lands the player on whichever disc shares this spot: disarm, or the dwell would restart
        /// under a player who never stepped here. Re-armed the moment the head leaves the disc.</summary>
        private void HandleFloorChanged()
        {
            _armed = false;
            ResetDwell();
        }

        private void Update()
        {
            if (!EnsureFloorValid())
            {
                return;
            }

            // §10.6: before alignment the rig sits wherever tracking put it, so "head over the disc"
            // means nothing.
            if (!CalibrationState.IsCalibrated)
            {
                ResetDwell();
                return;
            }

            Transform head = ResolveHead();
            if (head == null)
            {
                ResetDwell(); // admin observer (rig disabled) or the rig is not up yet
                return;
            }

            int myFloor = FloorState.Local;
            Transform disc = myFloor == _lowerFloor ? lowerDisc
                : myFloor == UpperFloor ? upperDisc
                : null;
            if (disc == null)
            {
                ResetDwell();
                return;
            }

            if (!IsOverDisc(disc, head.position))
            {
                _armed = true;
                ResetDwell();
                return;
            }

            if (!_armed)
            {
                ResetDwell(); // still standing on the disc we arrived at
                return;
            }

            if (!OwnsDisc(disc))
            {
                ShowBusy(disc);
                return;
            }

            if (_dwell <= 0f && chargeSound != null && !chargeSound.isPlaying)
            {
                chargeSound.Play();
            }

            _busyAnnounced = false;
            _dwell += Time.unscaledDeltaTime;

            int target = myFloor == _lowerFloor ? UpperFloor : _lowerFloor;

            if (_dwell < DwellSeconds)
            {
                float progress = Mathf.Clamp01(_dwell / DwellSeconds);
                _idleVisuals = false;
                Paint(disc, Color.Lerp(IdleColor, ChargedColor, progress));
                SetFill(disc, progress);
                int remaining = Mathf.CeilToInt(DwellSeconds - _dwell);
                SetNotice(target > myFloor
                    ? $"Üst kata geçiliyor… {remaining}"
                    : $"Alt kata geçiliyor… {remaining}");
                return;
            }

            if (!FloorState.MoveWithFade(target, "portal", 0.25f, 0.1f, 0.4f))
            {
                ResetDwell(); // nothing moved: do not spend the portal, do not play the sound
                return;
            }

            // Disarmed once the move is accepted: the fade puts the player on the paired disc, which
            // is inside this portal too.
            _armed = false;
            ResetDwell();
            if (teleportSound != null)
            {
                teleportSound.Play();
            }
        }

        /// <summary>Refreshes the cached floor and validates the root height; the portal DISABLES
        /// itself when its root sits on no floor level — a portal lifting the player to a height with
        /// no mesh under it is worse than a missing portal.</summary>
        private bool EnsureFloorValid()
        {
            if (_floorVersion == ArenaFloors.Version)
            {
                return true;
            }

            EnsureFloor();

            float y = transform.position.y;
            if (Mathf.Abs(y - ArenaFloors.HeightOf(_lowerFloor)) <= ArenaFloors.LevelToleranceMeters)
            {
                return true;
            }

            Debug.LogError(
                $"[FloorPortal] '{name}': kök yüksekliği {y:F2} m hiçbir kat zeminine oturmuyor — " +
                "portal kapatıldı.", this);
            enabled = false;
            return false;
        }

        private void EnsureFloor()
        {
            if (_floorVersion == ArenaFloors.Version)
            {
                return;
            }

            // Version read AFTER the query: the query itself may trigger the rebuild that bumps it,
            // and reading first would leave this cache stale for a frame on every rebuild.
            _lowerFloor = ArenaFloors.FloorAt(transform.position.y);
            _floorVersion = ArenaFloors.Version;
        }

        private static bool IsOverDisc(Transform disc, Vector3 point)
        {
            Vector3 center = disc.position;
            float dx = point.x - center.x;
            float dz = point.z - center.z;
            return dx * dx + dz * dz <= RadiusMeters * RadiusMeters;
        }

        /// <summary>Is the local player the owner of this disc: the LOWEST id among everyone standing
        /// over it, on any floor (the disc is one physical spot).</summary>
        private static bool OwnsDisc(Transform disc)
        {
            int myId = PlayerCombatState.Instance != null ? PlayerCombatState.Instance.PlayerId : 0;
            if (myId == 0)
            {
                return true; // server-less session: there is nobody to share the disc with
            }

            RemotePlayerRegistry registry = RemotePlayerRegistry.Instance;
            if (registry == null)
            {
                return true;
            }

            IdBuffer.Clear();
            registry.GetActivePlayerIds(IdBuffer);

            for (int i = 0; i < IdBuffer.Count; i++)
            {
                int id = IdBuffer[i];
                if (id >= myId)
                {
                    continue; // only a LOWER id can take the disc from us
                }

                if (!registry.GetInterpolatedPose(id, out Pose head, out _, out _))
                {
                    continue;
                }

                if (IsOverDisc(disc, head.position))
                {
                    return false;
                }
            }

            return true;
        }

        private void ShowBusy(Transform disc)
        {
            _dwell = 0f;
            _idleVisuals = false;
            SetFill(disc, 0f);
            Paint(disc, BusyColor);
            SetNotice("Portal meşgul");

            if (chargeSound != null && chargeSound.isPlaying)
            {
                chargeSound.Stop();
            }

            if (_busyAnnounced)
            {
                return;
            }

            _busyAnnounced = true;
            if (busySound != null)
            {
                busySound.Play();
            }
        }

        private void ResetDwell()
        {
            _dwell = 0f;
            _busyAnnounced = false;

            if (chargeSound != null && chargeSound.isPlaying)
            {
                chargeSound.Stop();
            }

            SetNotice("");

            // Idle is the common case (nobody on the portal): repainting it every frame would push a
            // property block per portal per frame for nothing.
            if (_idleVisuals)
            {
                return;
            }

            _idleVisuals = true;
            SetFill(lowerDisc, 0f);
            SetFill(upperDisc, 0f);
            Paint(lowerDisc, IdleColor);
            Paint(upperDisc, IdleColor);
        }

        private void SetFill(Transform disc, float progress)
        {
            Transform fill = disc == upperDisc ? upperFill : lowerFill;
            if (fill == null)
            {
                return;
            }

            // Y is the builder's thickness compensation (the disc parent is squashed): only the
            // radius animates.
            float f = Mathf.Clamp01(progress);
            fill.localScale = new Vector3(f, fill.localScale.y, f);
        }

        private void Paint(Transform disc, Color color)
        {
            if (disc == null)
            {
                return;
            }

            var renderer = disc.GetComponent<Renderer>();
            if (renderer == null)
            {
                return;
            }

            // Property block, not a material write: a shared material would tint every portal in the
            // arena at once.
            _block ??= new MaterialPropertyBlock();
            _block.SetColor(BaseColorId, color);
            renderer.SetPropertyBlock(_block);
        }

        /// <summary>Writes the HUD notice — only while this portal holds the player, and it clears its
        /// own text on exit so a second portal is not left shouting.</summary>
        private void SetNotice(string notice)
        {
            bool wanted = !string.IsNullOrEmpty(notice);
            if (!wanted && !_noticeShown)
            {
                return;
            }

            // Throttled, not once-per-enable: a mode change without a scene change destroys and
            // respawns the HUD (ModeHudSpawner), and a single search would leave this portal silent
            // for the rest of the scene. Per-frame scene-wide searches are still too costly.
            if (_hud == null)
            {
                if (Time.unscaledTime - _hudSearchTime < HudSearchIntervalSeconds)
                {
                    return;
                }

                _hudSearchTime = Time.unscaledTime;
                _hud = FindFirstObjectByType<ModeHudBase>();
            }

            if (_hud == null)
            {
                return; // lobby / no mode HUD: the portal still works, just silently
            }

            _hud.SetCenterNotice(notice);
            _noticeShown = wanted;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            upperHeight = Mathf.Max(0.5f, upperHeight);

            if (upperDisc != null)
            {
                upperDisc.localPosition = new Vector3(0f, upperHeight, 0f);
            }

            // The level list is derived from this height: it must not stay stale while the designer
            // drags the field.
            ArenaFloors.MarkDirty();
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan;
            DrawDiscGizmo(lowerDisc);
            DrawDiscGizmo(upperDisc);
        }

        private static void DrawDiscGizmo(Transform disc)
        {
            if (disc == null)
            {
                return;
            }

            const int steps = 24;
            Vector3 previous = disc.position + new Vector3(RadiusMeters, 0f, 0f);
            for (int i = 1; i <= steps; i++)
            {
                float angle = i / (float)steps * Mathf.PI * 2f;
                Vector3 next = disc.position +
                               new Vector3(Mathf.Cos(angle) * RadiusMeters, 0f, Mathf.Sin(angle) * RadiusMeters);
                Gizmos.DrawLine(previous, next);
                previous = next;
            }
        }
#endif

        /// <summary>HMD transform. ⚠️ Also the "are we a player" gate: on an admin observer the rig is
        /// disabled, so this stays <c>null</c> and the portal never fires (the
        /// <c>ObstacleViolationProbe</c> gate — Core cannot see <c>AppSession</c>).</summary>
        private Transform ResolveHead()
        {
            if (_rig != null && _rig.isActiveAndEnabled && _rig.centerEyeAnchor != null)
            {
                return _rig.centerEyeAnchor;
            }

            if (Time.unscaledTime - _rigSearchTime < RigSearchIntervalSeconds)
            {
                return null;
            }

            _rigSearchTime = Time.unscaledTime;
            _rig = FindFirstObjectByType<OVRCameraRig>();
            return _rig != null ? _rig.centerEyeAnchor : null;
        }
    }
}
