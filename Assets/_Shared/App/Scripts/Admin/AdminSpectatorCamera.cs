using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using VortexArena.Core.Arena;
using VortexArena.Core.Combat;
using VortexArena.Net;

namespace VortexArena.App.Admin
{
    /// <summary>
    /// The spectator camera's three modes (<see cref="AdminSession.CameraMode"/> selects one);
    /// per-mode input and placement live here.
    /// <list type="bullet">
    /// <item><b>POV:</b> the selected player's HEAD pose (arena → world). Without a pose it holds
    /// its last position — snapping to the origin would disorient the operator.</item>
    /// <item><b>Free:</b> WASD, Q/E up/down, look with the <b>right button held</b>, Shift ×3,
    /// wheel sets base speed. ⚠️ The cursor is NOT locked — the operator has one screen and needs
    /// the HUD buttons.</item>
    /// <item><b>Top-down:</b> orthographic, above the arena center, aligned to arena yaw; wheel
    /// zooms. ⚠️ The framing's ONLY source is the scene's <see cref="ArenaBoundary"/> (no default
    /// size), as is the height (the dimension file's <c>topViewHeight</c>, else
    /// <see cref="DefaultTopDownHeight"/>). An <see cref="ArenaRoof"/> is hidden on entering this
    /// mode (preference <c>AdminSession.Roof</c>) and restored on leaving.</item>
    /// </list>
    /// <para>Poses are read in <c>LateUpdate</c>, like <c>RemoteAvatar</c>, so the camera does not
    /// lag one frame behind.</para>
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class AdminSpectatorCamera : MonoBehaviour
    {
        /// <summary>Mouse sensitivity (degrees / pixel).</summary>
        private const float LookSensitivity = 0.12f;

        /// <summary>Speed multiplier while Shift is held.</summary>
        private const float BoostMultiplier = 3f;

        /// <summary>Free mode must not go below the floor.</summary>
        private const float MinHeight = 0.2f;

        /// <summary>
        /// POV camera's forward offset from the head pose (m), keeping head-mounted accessories
        /// (hat, glasses) out of frame. ⚠️ Raising it too far pushes the camera into walls.
        /// </summary>
        private const float PovForwardOffset = 0.1f;

        /// <summary>
        /// DEFAULT top-down camera height (m) — only affects clipping in orthographic. The venue's
        /// <c>topViewHeight</c> wins when set: 20 m can sit below the roof in a tall venue.
        /// </summary>
        private const float DefaultTopDownHeight = 20f;

        /// <summary>Top-down framing margin, so the arena edge does not touch the screen.</summary>
        private const float TopDownMargin = 1.08f;

        /// <summary>Top-down zoom limits (1 = the whole arena).</summary>
        private const float ZoomMin = 0.4f;
        private const float ZoomMax = 1.6f;

        private Camera _camera;
        private AdminCameraMode _appliedMode = (AdminCameraMode)(-1);

        /// <summary>Was the "no ArenaBoundary" warning already logged for this scene.</summary>
        private bool _warnedMissingBoundary;

        // Free mode state.
        private float _yaw;
        private float _pitch;

        // Top-down state.
        private float _zoom = 1f;

        private void Awake()
        {
            _camera = GetComponent<Camera>();
        }

        /// <summary>New scene adopted: re-apply the mode (the arena boundary changed).</summary>
        public void OnSceneAdopted()
        {
            _appliedMode = (AdminCameraMode)(-1);
            _warnedMissingBoundary = false;
        }

        private void LateUpdate()
        {
            AdminCameraMode mode = AdminSession.CameraMode;
            if (mode != _appliedMode)
            {
                EnterMode(mode);
                _appliedMode = mode;
            }

            ApplyAudioFocus(mode);

            switch (mode)
            {
                case AdminCameraMode.Pov:
                    DrivePov();
                    break;
                case AdminCameraMode.Free:
                    DriveFree();
                    break;
                default:
                    DriveTopDown();
                    break;
            }
        }

        /// <summary>Once per mode entry: projection, roof visibility and initial angles.</summary>
        private void EnterMode(AdminCameraMode mode)
        {
            _camera.orthographic = mode == AdminCameraMode.TopDown;

            // Roof hides on entering top-down and returns on leaving (preference: AdminSession.Roof).
            AdminSpectator.RefreshRoof();

            if (mode == AdminCameraMode.Free)
            {
                // Continue from the entry angle so free mode does not jump.
                Vector3 euler = transform.eulerAngles;
                _yaw = euler.y;
                _pitch = NormalizePitch(euler.x);
            }
        }

        // ---------------------------------------------------------------- audio focus

        /// <summary>
        /// Points the spectator's ear where the camera looks
        /// (<see cref="RemoteShotFx.SpectatorAudioFocus"/>).
        /// <para>Focus exists <b>only in POV</b>: the watched player's weapon at full volume, the
        /// rest quieter — at equal volume the operator cannot tell which shot is the watched
        /// player's. ⚠️ Quieted, NOT muted: a firefight across the arena must still be audible.</para>
        /// <para>⚠️ <b>No focus in top-down/free</b> (<c>null</c>) — the operator watches the whole
        /// field. Same for POV with no player selected: there is nobody to foreground.</para>
        /// <para>⚠️ Asked every frame, NOT via <c>AdminSession.Changed</c>: both inputs (mode +
        /// selection) change during a live match and one missed event would permanently play the
        /// WRONG player's weapon. Same rationale as <c>RemoteAvatar</c> name labels.</para>
        /// <para>⚠️ The only writer. The spectator camera exists only in the admin role, so the
        /// player client never writes focus (null = no filter) and hears every shot.</para>
        /// </summary>
        private static void ApplyAudioFocus(AdminCameraMode mode)
        {
            int selected = AdminSession.SelectedPlayerId;
            RemoteShotFx.SpectatorAudioFocus =
                mode == AdminCameraMode.Pov && selected != 0 ? selected : (int?)null;
        }

        // ------------------------------------------------------------------- POV

        private void DrivePov()
        {
            int playerId = AdminSession.SelectedPlayerId;
            RemotePlayerRegistry registry = RemotePlayerRegistry.Instance;
            if (playerId == 0 || registry == null ||
                !registry.GetInterpolatedPose(playerId, out Pose head, out _, out _))
            {
                return; // no pose: hold the last position (the HUD says "poz yok")
            }

            Pose world = ArenaSpace.ArenaToWorld(head);
            // Slightly in FRONT of the head pose: accessories are parented to the head bone and
            // would fill the frame at the exact head point.
            Vector3 position = world.position + world.rotation * Vector3.forward * PovForwardOffset;
            transform.SetPositionAndRotation(position, world.rotation);
        }

        // ------------------------------------------------------------------- free

        private void DriveFree()
        {
            Keyboard keyboard = Keyboard.current;
            Mouse mouse = Mouse.current;

            if (mouse != null)
            {
                // Look ONLY while the right button is held; the cursor stays free for the HUD.
                if (mouse.rightButton.isPressed)
                {
                    Vector2 delta = mouse.delta.ReadValue();
                    _yaw += delta.x * LookSensitivity;
                    _pitch = Mathf.Clamp(_pitch - delta.y * LookSensitivity, -89f, 89f);
                }

                float scroll = mouse.scroll.ReadValue().y;
                if (!Mathf.Approximately(scroll, 0f))
                {
                    // Wheel steps the base speed (persisted as a preference).
                    AdminSession.FreeSpeed += Mathf.Sign(scroll) * 0.5f;
                }
            }

            transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);

            if (keyboard == null)
            {
                return;
            }

            var input = Vector3.zero;
            if (keyboard.wKey.isPressed) input += transform.forward;
            if (keyboard.sKey.isPressed) input -= transform.forward;
            if (keyboard.dKey.isPressed) input += transform.right;
            if (keyboard.aKey.isPressed) input -= transform.right;
            if (keyboard.eKey.isPressed) input += Vector3.up;
            if (keyboard.qKey.isPressed) input -= Vector3.up;

            if (input.sqrMagnitude < 1e-6f)
            {
                return;
            }

            float speed = AdminSession.FreeSpeed *
                          (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed
                              ? BoostMultiplier
                              : 1f);

            Vector3 position = transform.position + input.normalized * (speed * Time.unscaledDeltaTime);
            position.y = Mathf.Max(MinHeight, position.y);
            transform.position = position;
        }

        // --------------------------------------------------------------- top-down

        private void DriveTopDown()
        {
            Mouse mouse = Mouse.current;
            if (mouse != null)
            {
                float scroll = mouse.scroll.ReadValue().y;
                if (!Mathf.Approximately(scroll, 0f))
                {
                    _zoom = Mathf.Clamp(_zoom - Mathf.Sign(scroll) * 0.1f, ZoomMin, ZoomMax);
                }
            }

            if (!TryResolveArena(out Vector3 center, out float yaw, out Vector2 halfExtents, out float height))
            {
                // ⚠️ No invented framing size: look down from above the world origin and leave the
                // orthographic size as is (the operator adjusts with the wheel).
                WarnMissingBoundary();
                transform.SetPositionAndRotation(
                    Vector3.up * DefaultTopDownHeight,
                    Quaternion.Euler(90f, 0f, 0f));
                return;
            }

            float aspect = _camera.aspect > 0.01f ? _camera.aspect : 16f / 9f;
            float sizeFromZ = halfExtents.y;
            float sizeFromX = halfExtents.x / aspect;
            _camera.orthographicSize = Mathf.Max(sizeFromZ, sizeFromX) * TopDownMargin * _zoom;

            transform.SetPositionAndRotation(
                center + Vector3.up * height,
                Quaternion.Euler(90f, yaw, 0f));
        }

        /// <summary>
        /// Arena center/yaw/half extents and camera height — the ONLY source is the scene's
        /// <see cref="ArenaBoundary"/>. ⚠️ <b>No default size:</b> an invented arena size produces a
        /// wrong framing that looks right; the component is mandatory in every arena scene.
        /// <para>
        /// Height is the exception (<see cref="DefaultTopDownHeight"/>): it does not affect the
        /// framing, so leaving it unset is a missing preference, not a setup error.
        /// </para>
        /// </summary>
        private bool TryResolveArena(
            out Vector3 center,
            out float yaw,
            out Vector2 halfExtents,
            out float height)
        {
            ArenaBoundary boundary = AdminSpectator.Instance != null
                ? AdminSpectator.Instance.Boundary
                : null;

            if (boundary == null)
            {
                center = Vector3.zero;
                yaw = 0f;
                halfExtents = Vector2.zero;
                height = DefaultTopDownHeight;
                return false;
            }

            Transform origin = boundary.transform;

            // ⚠️ The center is the boundary's LOCAL center, NOT the transform position: in polygonal
            // arenas the bounding box center does not sit on the transform, and using the position
            // shifts the framing outside the arena. LocalCenter is zero for rectangles.
            Vector2 localCenter = boundary.LocalCenter;
            center = origin.TransformPoint(new Vector3(localCenter.x, 0f, localCenter.y));
            yaw = origin.eulerAngles.y;
            halfExtents = boundary.HalfExtents;

            float fromPlan = boundary.TopDownHeight;
            height = fromPlan > 0f ? fromPlan : DefaultTopDownHeight;
            return true;
        }

        /// <summary>
        /// Warns ONCE per scene about a missing <see cref="ArenaBoundary"/>, so a setup error does
        /// not hide as "slightly off framing". Silent in the lobby, where no arena is expected.
        /// </summary>
        private void WarnMissingBoundary()
        {
            if (_warnedMissingBoundary)
            {
                return;
            }

            _warnedMissingBoundary = true;

            string scene = SceneManager.GetActiveScene().name;
            if (scene == AppSession.SceneLobby)
            {
                return;
            }

            Debug.LogWarning($"[AdminSpectatorCamera] '{scene}' sahnesinde ArenaBoundary yok — " +
                             "kuş bakışı kadrajı ölçüsüz kaldı. Arena sahnesinde ArenaBoundary ZORUNLUDUR.");
        }

        private static float NormalizePitch(float pitch)
        {
            // eulerAngles gives 0..360; bring it into -89..89.
            return pitch > 180f ? pitch - 360f : pitch;
        }
    }
}
