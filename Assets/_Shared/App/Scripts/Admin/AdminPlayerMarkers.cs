using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VortexArena.Core.Arena;
using VortexArena.Net;

namespace VortexArena.App.Admin
{
    /// <summary>
    /// World-space marker per player: a <b>ring around their feet</b> and a <b>name label below
    /// it</b>.
    /// <para>
    /// ⚠️ Does NOT touch <c>RemoteAvatar</c> — player-side visuals must not change for the admin.
    /// Its objects live under <see cref="AdminSpectator"/>'s persistent root, so scene changes need
    /// no rebuild (positions come from arena space each frame).
    /// </para>
    /// <para>
    /// <b>Ring:</b> the head pose's x/z lowered to y=0.02 in arena space, so it stays put when the
    /// player bends over. <b>Label:</b> faces the camera and is offset along -up, so it reads under
    /// the circle in both top-down and free mode.
    /// </para>
    /// </summary>
    public class AdminPlayerMarkers : MonoBehaviour
    {
        /// <summary>Ring diameter (m) — a bit wider than shoulder width.</summary>
        private const float RingDiameter = 0.9f;

        /// <summary>Ring canvas pixel size; with the world scale it yields the diameter.</summary>
        private const float RingPixels = 300f;

        /// <summary>Label's screen-space distance from the ring (m).</summary>
        private const float LabelOffset = 0.62f;

        /// <summary>Lift above the floor (m) — avoids z-fighting.</summary>
        private const float FloorLift = 0.02f;

        /// <summary>Growth factor of the selected player's ring.</summary>
        private const float SelectedScale = 1.18f;

        /// <summary>Color multiplier for a dead marker (the same as RemoteAvatar).</summary>
        private const float DeadColorScale = 0.35f;

        private class Marker
        {
            public GameObject root;
            public AdminPlayerMarker view;
            public Transform ring;
            public Image ringImage;
            public Transform label;
            public TextMeshProUGUI labelText;
        }

        private readonly Dictionary<int, Marker> _markers = new Dictionary<int, Marker>();

        /// <summary>Marker prefab, loaded once from <c>Resources</c> — this component is added from
        /// code, so <c>[SerializeField]</c> wiring is impossible.</summary>
        private AdminPlayerMarker _markerPrefab;

        private bool _prefabMissingLogged;
        private readonly List<int> _idScratch = new List<int>();
        private bool _subscribed;

        private void Start()
        {
            RemotePlayerRegistry registry = RemotePlayerRegistry.Instance;
            if (registry == null)
            {
                // ArenaClient's bootstrap runs before us — this does not happen in practice.
                Debug.LogWarning("[AdminPlayerMarkers] RemotePlayerRegistry yok; işaretçiler devre dışı.");
                enabled = false;
                return;
            }

            registry.OnRemoteJoined += Spawn;
            registry.OnRemoteLeft += Despawn;
            _subscribed = true;

            registry.GetActivePlayerIds(_idScratch);
            for (int i = 0; i < _idScratch.Count; i++)
            {
                Spawn(_idScratch[i]);
            }
        }

        private void OnDestroy()
        {
            if (_subscribed)
            {
                RemotePlayerRegistry registry = RemotePlayerRegistry.Instance;
                if (registry != null)
                {
                    registry.OnRemoteJoined -= Spawn;
                    registry.OnRemoteLeft -= Despawn;
                }

                _subscribed = false;
            }

            foreach (KeyValuePair<int, Marker> kv in _markers)
            {
                if (kv.Value.root != null)
                {
                    Destroy(kv.Value.root);
                }
            }

            _markers.Clear();
        }

        private void LateUpdate()
        {
            RemotePlayerRegistry registry = RemotePlayerRegistry.Instance;
            Camera camera = AdminSpectator.Instance != null ? AdminSpectator.Instance.Camera : null;
            if (registry == null || camera == null)
            {
                return;
            }

            bool ringsVisible = AdminSession.MarkersVisibleNow();
            bool labelsVisible = ringsVisible && AdminSession.Nameplates;
            AdminRoster roster = AdminRoster.Instance;
            int selectedId = AdminSession.SelectedPlayerId;

            foreach (KeyValuePair<int, Marker> kv in _markers)
            {
                Marker marker = kv.Value;
                if (marker.root == null)
                {
                    continue;
                }

                if (!ringsVisible ||
                    !registry.GetInterpolatedPose(kv.Key, out Pose head, out _, out _))
                {
                    if (marker.root.activeSelf)
                    {
                        marker.root.SetActive(false);
                    }

                    continue;
                }

                if (!marker.root.activeSelf)
                {
                    marker.root.SetActive(true);
                }

                // Footprint: the head pose's x/z, just above the arena floor.
                var floorArena = new Pose(
                    new Vector3(head.position.x, FloorLift, head.position.z), Quaternion.identity);
                Vector3 floorWorld = ArenaSpace.ArenaToWorld(floorArena).position;

                bool selected = kv.Key == selectedId;
                AdminPlayerView view = roster != null ? roster.Find(kv.Key) : null;
                bool alive = view != null ? view.alive : registry.IsAlive(kv.Key);

                // No violation for a dead player: the penalty already stopped (server `Alive` gate,
                // §10.9) and there is nothing for the operator to act on.
                AdminViolationKind violation = alive
                    ? AdminViolations.Of(kv.Key)
                    : AdminViolationKind.None;

                // ⚠️ The violation color OVERRIDES the selection highlight and stays that way: a
                // selection is a preference already shown three ways (grown ring, thick sprite,
                // bottom bar name), while this is the violation's only visible channel.
                Color color = violation != AdminViolationKind.None
                    ? AdminViolations.Blink(violation)
                    : ResolveColor(view, selected, alive);

                // The ring lies flat on the floor (x=90). Since it is a circle, yaw is irrelevant.
                marker.ring.SetPositionAndRotation(floorWorld, Quaternion.Euler(90f, 0f, 0f));
                marker.ring.localScale = Vector3.one *
                    (RingDiameter / RingPixels * (selected ? SelectedScale : 1f));

                if (marker.ringImage != null)
                {
                    // ⚠️ Color goes ONLY through Image.color: the ring draws via CanvasRenderer,
                    // where MaterialPropertyBlock/shader parameters are never applied.
                    marker.ringImage.color = color;
                }

                if (marker.view != null)
                {
                    marker.view.SetSelected(selected);
                }

                if (marker.label == null)
                {
                    continue;
                }

                if (marker.label.gameObject.activeSelf != labelsVisible)
                {
                    marker.label.gameObject.SetActive(labelsVisible);
                }

                if (!labelsVisible)
                {
                    continue;
                }

                // BELOW the ring in screen space: the negative of the camera's up vector.
                Transform cameraTransform = camera.transform;
                Vector3 labelPosition = floorWorld - cameraTransform.up * LabelOffset;
                Vector3 toCamera = labelPosition - cameraTransform.position;
                marker.label.SetPositionAndRotation(
                    labelPosition,
                    toCamera.sqrMagnitude > 1e-6f
                        ? Quaternion.LookRotation(toCamera, cameraTransform.up)
                        : marker.label.rotation);

                if (marker.labelText != null)
                {
                    marker.labelText.color = ResolveLabelColor(view, alive);
                    marker.labelText.text = BuildLabel(kv.Key, view, alive, violation);
                }
            }
        }

        // ------------------------------------------------------------------- setup

        private void Spawn(int playerId)
        {
            if (_markers.ContainsKey(playerId))
            {
                return;
            }

            if (_markerPrefab == null)
            {
                _markerPrefab = Resources.Load<AdminPlayerMarker>(AdminPlayerMarker.ResourcePath);
                if (_markerPrefab == null)
                {
                    if (!_prefabMissingLogged)
                    {
                        _prefabMissingLogged = true;
                        Debug.LogError(
                            $"[AdminPlayerMarkers] '{AdminPlayerMarker.ResourcePath}' prefabı " +
                            "bulunamadı — oyuncu halkaları çizilemeyecek.");
                    }

                    return;
                }
            }

            AdminPlayerMarker instance = Instantiate(_markerPrefab, transform, false);
            instance.name = $"[AdminMarker_{playerId}]";

            _markers.Add(playerId, new Marker
            {
                root = instance.gameObject,
                view = instance,
                ring = instance.Ring,
                ringImage = instance.RingImage,
                label = instance.Label,
                labelText = instance.LabelText
            });
        }

        private void Despawn(int playerId)
        {
            if (!_markers.TryGetValue(playerId, out Marker marker))
            {
                return;
            }

            _markers.Remove(playerId);
            if (marker.root != null)
            {
                Destroy(marker.root);
            }
        }

        // ---------------------------------------------------------------- visuals

        /// <summary>
        /// Label color: <b>always the team color</b> (dimmed when dead). ⚠️ The selection highlight
        /// is the RING's job — recoloring the name would break the at-a-glance "which team" answer
        /// for exactly one player every time.
        /// </summary>
        private static Color ResolveLabelColor(AdminPlayerView view, bool alive)
        {
            Color team = UiKit.TeamColor(view != null ? view.team : "");
            return alive ? team : UiKit.Dim(team, DeadColorScale);
        }

        private static Color ResolveColor(AdminPlayerView view, bool selected, bool alive)
        {
            if (selected)
            {
                return alive ? UiKit.Accent : UiKit.Dim(UiKit.Accent, DeadColorScale);
            }

            Color team = UiKit.TeamColor(view != null ? view.team : "");
            return alive ? team : UiKit.Dim(team, DeadColorScale);
        }

        /// <summary>
        /// The name label, with the violation kind appended as text: color and frequency separate
        /// the states but require memorization, "DUVAR" / "ALAN DIŞI" do not.
        /// <para>Subject to the caller's <c>labelsVisible</c> gate; with labels off, the ring color
        /// carries the violation.</para>
        /// </summary>
        private static string BuildLabel(int playerId, AdminPlayerView view, bool alive,
            AdminViolationKind violation)
        {
            string name = view != null && !string.IsNullOrEmpty(view.name)
                ? view.name
                : $"Oyuncu {playerId}";

            if (!alive)
            {
                return $"{name} (ölü)";
            }

            string label = view != null
                ? $"{name}  {Mathf.RoundToInt(view.hp)}"
                : name;

            string violationLabel = AdminViolations.Label(violation);
            return string.IsNullOrEmpty(violationLabel) ? label : $"{label}  {violationLabel}";
        }
    }
}
