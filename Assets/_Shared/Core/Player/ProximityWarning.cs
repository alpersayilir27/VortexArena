using System.Collections.Generic;
using UnityEngine;
using VortexArena.Core.Arena;
using VortexArena.Net;

namespace VortexArena.Core.Player
{
    /// <summary>Free-roam collision avoidance: warns about REAL bodies approaching the local player's
    /// HMD.</summary>
    /// <remarks>
    /// A 12x12 m arena with 10 players gives ~11.8 m²/player; at that density two players being within
    /// 1 m is routine, and the HMD's narrow lower/side field of view does not show it. The warning has two
    /// channels:
    /// <list type="bullet">
    /// <item>Visual: a halo at the remote player's position, visible THROUGH WALLS
    /// (<c>VortexArena/ProximityHalo</c>, ZTest Always).</item>
    /// <item>Haptic: the controller on the SIDE the danger comes from vibrates (OVRInput).</item>
    /// </list>
    /// <para>⚠️ Dead players are NOT filtered out. Respawn here is a STATE change, not a position change
    /// (protocol §respawn) — a dead player's physical body stays on the floor, so the collision risk is
    /// the same as while alive.</para>
    /// <para>Poses arrive in arena space and are converted to world via <see cref="ArenaSpace"/>. One
    /// instance per scene suffices (can be added to the PoseSync object).</para>
    /// </remarks>
    public class ProximityWarning : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("HMD transform (CenterEyeAnchor). Boşsa Camera.main kullanılır.")]
        [SerializeField] private Transform head;

        [Tooltip("VortexArena/ProximityHalo materyali. Boşsa görsel uyarı kapalıdır.")]
        [SerializeField] private Material haloMaterial;

        [Header("Mesafeler (metre, yatay düzlemde)")]
        [Tooltip("Halkanın belirmeye başladığı mesafe.")]
        [SerializeField] private float warnDistance = 1.2f;

        [Tooltip("Tam yoğunluk + haptik uyarının devreye girdiği mesafe.")]
        [SerializeField] private float criticalDistance = 0.8f;

        [Header("Görsel")]
        [SerializeField] private float haloDiameter = 1.1f;
        [Tooltip("Halkanın yerel oyuncunun göz hizasına göre dikey ofseti.")]
        [SerializeField] private float haloHeightOffset = -0.15f;
        [Range(0f, 4f)]
        [SerializeField] private float maxHaloIntensity = 1.6f;

        [Header("Haptik")]
        [SerializeField] private bool hapticsEnabled = true;
        [Range(0f, 1f)]
        [SerializeField] private float maxHapticAmplitude = 0.65f;
        [Range(0f, 1f)]
        [SerializeField] private float hapticFrequency = 0.35f;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private const float CameraRetryIntervalSeconds = 1f;

        /// <summary>Distance at which haptics reaches full power (below the critical distance).</summary>
        private const float HapticPeakDistance = 0.35f;

        private readonly List<int> _activeIds = new List<int>();
        private readonly Dictionary<int, Renderer> _halos = new Dictionary<int, Renderer>();
        private readonly List<int> _staleIds = new List<int>();

        private MaterialPropertyBlock _propertyBlock;
        private Camera _mainCamera;
        private float _cameraRetryTimer;
        private bool _vibratingLeft;
        private bool _vibratingRight;

        /// <summary>Horizontal distance to the nearest remote player; +infinity if there is none.</summary>
        public float NearestDistance { get; private set; } = float.PositiveInfinity;

        /// <summary>Is at least one player within the critical distance.</summary>
        public bool IsCritical => NearestDistance <= criticalDistance;

        private void Awake()
        {
            _propertyBlock = new MaterialPropertyBlock();
            ResolveHead();
        }

        private void OnDisable()
        {
            StopVibration(OVRInput.Controller.LTouch, ref _vibratingLeft);
            StopVibration(OVRInput.Controller.RTouch, ref _vibratingRight);

            foreach (KeyValuePair<int, Renderer> kv in _halos)
            {
                if (kv.Value != null)
                {
                    kv.Value.enabled = false;
                }
            }

            NearestDistance = float.PositiveInfinity;
        }

        private void ResolveHead()
        {
            if (head != null)
            {
                return;
            }

            _cameraRetryTimer -= Time.deltaTime;
            if (_cameraRetryTimer > 0f)
            {
                return;
            }

            _cameraRetryTimer = CameraRetryIntervalSeconds;
            _mainCamera = Camera.main;
            if (_mainCamera != null)
            {
                head = _mainCamera.transform;
            }
        }

        // RemoteAvatar applies its poses in LateUpdate; we read in LateUpdate (after it) so the warning
        // stays current in the same frame.
        private void LateUpdate()
        {
            ResolveHead();

            RemotePlayerRegistry registry = RemotePlayerRegistry.Instance;
            if (head == null || registry == null)
            {
                HideAll();
                return;
            }

            registry.GetActivePlayerIds(_activeIds);

            Vector3 headPosition = head.position;
            float nearest = float.PositiveInfinity;
            Vector3 nearestDirection = Vector3.zero;

            for (int i = 0; i < _activeIds.Count; i++)
            {
                int playerId = _activeIds[i];

                // NOTE: no IsAlive check — a dead player's body is still on the floor.
                if (!registry.GetInterpolatedPose(playerId, out Pose headPose, out _, out _))
                {
                    SetHaloVisible(playerId, false);
                    continue;
                }

                Vector3 otherPosition = ArenaSpace.ArenaToWorld(headPose).position;

                Vector3 offset = otherPosition - headPosition;
                offset.y = 0f;                       // height difference does not affect collision
                float distance = offset.magnitude;

                if (distance < nearest)
                {
                    nearest = distance;
                    nearestDirection = offset;
                }

                if (distance > warnDistance)
                {
                    SetHaloVisible(playerId, false);
                    continue;
                }

                UpdateHalo(playerId, otherPosition, headPosition, Intensity(distance));
            }

            PruneHalos();

            NearestDistance = nearest;
            UpdateHaptics(nearest, nearestDirection);
        }

        /// <summary>0 at warnDistance, 1 at criticalDistance and below.</summary>
        private float Intensity(float distance)
        {
            if (warnDistance <= criticalDistance)
            {
                return distance <= criticalDistance ? 1f : 0f;
            }

            return Mathf.Clamp01((warnDistance - distance) / (warnDistance - criticalDistance));
        }

        private void UpdateHalo(int playerId, Vector3 otherPosition, Vector3 headPosition, float intensity)
        {
            if (haloMaterial == null)
            {
                return;
            }

            Renderer halo = GetOrCreateHalo(playerId);
            if (halo == null)
            {
                return;
            }

            Transform t = halo.transform;
            t.position = new Vector3(otherPosition.x, otherPosition.y + haloHeightOffset, otherPosition.z);

            // Face the local player (billboard) so the halo reads as a full circle from any angle.
            Vector3 toViewer = headPosition - t.position;
            if (toViewer.sqrMagnitude > 1e-6f)
            {
                t.rotation = Quaternion.LookRotation(-toViewer.normalized, Vector3.up);
            }

            t.localScale = Vector3.one * haloDiameter;

            halo.enabled = true;
            halo.GetPropertyBlock(_propertyBlock);
            Color color = haloMaterial.HasProperty(BaseColorId)
                ? haloMaterial.GetColor(BaseColorId)
                : Color.white;
            color.a = intensity * maxHaloIntensity;
            _propertyBlock.SetColor(BaseColorId, color);
            halo.SetPropertyBlock(_propertyBlock);
        }

        private Renderer GetOrCreateHalo(int playerId)
        {
            if (_halos.TryGetValue(playerId, out Renderer existing) && existing != null)
            {
                return existing;
            }

            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "ProximityHalo_" + playerId;
            quad.transform.SetParent(transform, false);
            quad.hideFlags = HideFlags.DontSave;

            // The primitive's collider MUST go: otherwise the warning halo eats the weapon raycast
            // (Weapon.cs, unmasked Physics.Raycast).
            Collider collider = quad.GetComponent<Collider>();
            if (collider != null)
            {
                Destroy(collider);
            }

            var renderer = quad.GetComponent<Renderer>();
            renderer.sharedMaterial = haloMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            renderer.enabled = false;

            _halos[playerId] = renderer;
            return renderer;
        }

        private void SetHaloVisible(int playerId, bool visible)
        {
            if (_halos.TryGetValue(playerId, out Renderer halo) && halo != null)
            {
                halo.enabled = visible;
            }
        }

        /// <summary>Removes the halos of players who left.</summary>
        private void PruneHalos()
        {
            _staleIds.Clear();

            foreach (KeyValuePair<int, Renderer> kv in _halos)
            {
                if (kv.Value == null || !_activeIds.Contains(kv.Key))
                {
                    _staleIds.Add(kv.Key);
                }
            }

            for (int i = 0; i < _staleIds.Count; i++)
            {
                int id = _staleIds[i];
                if (_halos.TryGetValue(id, out Renderer halo) && halo != null)
                {
                    Destroy(halo.gameObject);
                }

                _halos.Remove(id);
            }
        }

        private void HideAll()
        {
            foreach (KeyValuePair<int, Renderer> kv in _halos)
            {
                if (kv.Value != null)
                {
                    kv.Value.enabled = false;
                }
            }

            NearestDistance = float.PositiveInfinity;
            StopVibration(OVRInput.Controller.LTouch, ref _vibratingLeft);
            StopVibration(OVRInput.Controller.RTouch, ref _vibratingRight);
        }

        /// <summary>Vibrates the controller on the side the danger comes from, so the player knows which
        /// way to move without looking.</summary>
        private void UpdateHaptics(float nearest, Vector3 direction)
        {
            if (!hapticsEnabled || nearest > criticalDistance || direction.sqrMagnitude < 1e-6f)
            {
                StopVibration(OVRInput.Controller.LTouch, ref _vibratingLeft);
                StopVibration(OVRInput.Controller.RTouch, ref _vibratingRight);
                return;
            }

            float t = criticalDistance <= HapticPeakDistance
                ? 1f
                : Mathf.Clamp01((criticalDistance - nearest) / (criticalDistance - HapticPeakDistance));
            float amplitude = Mathf.Lerp(0.15f, maxHapticAmplitude, t);

            bool right = Vector3.Dot(head.right, direction.normalized) >= 0f;
            if (right)
            {
                StopVibration(OVRInput.Controller.LTouch, ref _vibratingLeft);
                OVRInput.SetControllerVibration(hapticFrequency, amplitude, OVRInput.Controller.RTouch);
                _vibratingRight = true;
            }
            else
            {
                StopVibration(OVRInput.Controller.RTouch, ref _vibratingRight);
                OVRInput.SetControllerVibration(hapticFrequency, amplitude, OVRInput.Controller.LTouch);
                _vibratingLeft = true;
            }
        }

        private static void StopVibration(OVRInput.Controller controller, ref bool flag)
        {
            if (!flag)
            {
                return;
            }

            OVRInput.SetControllerVibration(0f, 0f, controller);
            flag = false;
        }
    }
}
