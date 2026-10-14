using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VortexArena.Core.Arena;
using VortexArena.Core.Combat;
using VortexArena.Core.UI;
using VortexArena.Net;
using VortexArena.Protocol;

namespace VortexArena.Modes.Zombie
{
    /// <summary>World-space marker over every downed squad mate: a "YARDIM" plate with the distance and
    /// the pick-up progress, plus a ring on the floor where they fell.</summary>
    /// <remarks>Built and destroyed by <see cref="ZombieClientController"/> (one per headset). Its own
    /// root, NOT a child of the HUD: the HUD is head-locked while these belong in the arena.
    /// <para>⚠️ Drawn through <see cref="UiDrawOnTop"/>: a downed mate is usually behind a wall, which is
    /// exactly when the marker is needed — depth-tested UI would be swallowed without any error.</para>
    /// <para>⚠️ Pooled per <c>playerId</c> and never rebuilt: a marker per frame would allocate a canvas
    /// and a font mesh on a 72 Hz headset.</para></remarks>
    internal class ZombieDownedMarkers : MonoBehaviour
    {
        /// <summary>Canvas units per metre — the plate is authored in "pixels" and scaled down, so the
        /// font sizes read like the rest of the kit (same trade as the weapon lock plate).</summary>
        private const float UnitsPerMeter = 1000f;

        /// <summary>Plate height above the mate's head pose (m).</summary>
        private const float PlateLift = 0.45f;

        /// <summary>Ring diameter (m) — the same footprint the admin markers use.</summary>
        private const float RingDiameter = 0.9f;

        private const float RingPixels = 300f;

        /// <summary>Ring lift above the floor (m) — avoids z-fighting with the floor mesh.</summary>
        private const float FloorLift = 0.02f;

        // Plate metrics (px at UnitsPerMeter).
        private const float PlateW = 420f;
        private const float PlateH = 150f;
        private const float BarH = 18f;
        private const float BarInset = 24f;

        /// <summary>Alpha while the local player is down too: they still have to find the mate (someone
        /// else may get them both up), but it is not their job this second.</summary>
        private const float DimAlpha = 0.4f;

        private class Marker
        {
            public GameObject root;
            public CanvasGroup group;
            public Transform plate;
            public Transform ring;
            public TMP_Text distance;
            public RectTransform fill;
            public float trackWidth;

            /// <summary>Last distance written, in decimetres — the text is only rebuilt when the
            /// number the player reads actually changes.</summary>
            public int shownDecimetres = -1;
        }

        private readonly Dictionary<int, Marker> _markers = new Dictionary<int, Marker>();

        private PlayerInfo[] _roster = System.Array.Empty<PlayerInfo>();

        /// <summary>Roster from the HUD's <c>lobby_state</c> (§10.2) — names, alive flags and who is a
        /// participant at all.</summary>
        public void SetRoster(PlayerInfo[] roster)
        {
            _roster = roster ?? System.Array.Empty<PlayerInfo>();
        }

        private void OnDestroy()
        {
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
            Camera eye = Camera.main;
            if (registry == null || eye == null)
            {
                HideAll();
                return;
            }

            PlayerCombatState combat = PlayerCombatState.Instance;
            float alpha = combat != null && !combat.IsAlive ? DimAlpha : 1f;
            int self = combat != null ? combat.PlayerId : 0;
            Transform eyeTransform = eye.transform;

            for (int i = 0; i < _roster.Length; i++)
            {
                PlayerInfo info = _roster[i];
                if (!IsDownedMate(info, self)
                    || !registry.GetInterpolatedPose(info.playerId, out Pose head, out _, out _))
                {
                    SetVisible(info != null ? info.playerId : 0, false);
                    continue;
                }

                Marker marker = Resolve(info.playerId);
                if (marker == null)
                {
                    continue;
                }

                SetMarkerActive(marker, true);
                marker.group.alpha = alpha;

                Vector3 plateWorld = ArenaSpace.ArenaToWorld(head.position + Vector3.up * PlateLift);

                // Faces the camera: UI is visible from its −Z side, so +Z must point AWAY from the eye.
                Vector3 away = plateWorld - eyeTransform.position;
                marker.plate.SetPositionAndRotation(
                    plateWorld,
                    away.sqrMagnitude > 1e-6f
                        ? Quaternion.LookRotation(away, eyeTransform.up)
                        : marker.plate.rotation);

                float distance = away.magnitude;
                int decimetres = Mathf.RoundToInt(distance * 10f);
                if (marker.shownDecimetres != decimetres)
                {
                    marker.shownDecimetres = decimetres;
                    marker.distance.text = (decimetres * 0.1f).ToString("0.0") + " m";
                }

                float progress = Mathf.Clamp01(TeammateRevive.AllyProgress(info.playerId));
                marker.fill.sizeDelta = new Vector2(marker.trackWidth * progress, BarH);

                // The ring marks the spot on the FLOOR the mate fell on; on another storey it would
                // float through the ceiling, so it is left to the plate there.
                bool sameFloor = FloorState.PlayerFloor(info.playerId) == FloorState.Local;
                if (marker.ring.gameObject.activeSelf != sameFloor)
                {
                    marker.ring.gameObject.SetActive(sameFloor);
                }

                if (!sameFloor)
                {
                    continue;
                }

                float floorY = ArenaFloors.HeightOf(FloorState.PlayerFloor(info.playerId)) + FloorLift;
                Vector3 ringWorld = ArenaSpace.ArenaToWorld(
                    new Vector3(head.position.x, floorY, head.position.z));

                // Flat on the floor (x=90); it is a circle, so yaw does not matter.
                marker.ring.SetPositionAndRotation(ringWorld, Quaternion.Euler(90f, 0f, 0f));
            }
        }

        /// <summary>A mate worth marking: a calibrated participant, still on the server, who is down.
        /// ⚠️ Uncalibrated records are skipped — they are not in the fight (§10.6), and marking them
        /// would send the squad after someone who cannot be revived.</summary>
        private static bool IsDownedMate(PlayerInfo info, int self)
        {
            return info != null
                   && info.playerId != self
                   && info.role != "admin" // no protocol constant for the role strings (§2)
                   && info.inMatch
                   && info.calibrated
                   && !info.alive
                   && info.connection != ArenaProtocol.CONNECTION_LEFT;
        }

        private void HideAll()
        {
            foreach (KeyValuePair<int, Marker> kv in _markers)
            {
                SetMarkerActive(kv.Value, false);
            }
        }

        private void SetVisible(int playerId, bool visible)
        {
            if (playerId != 0 && _markers.TryGetValue(playerId, out Marker marker))
            {
                SetMarkerActive(marker, visible);
            }
        }

        private static void SetMarkerActive(Marker marker, bool active)
        {
            if (marker.root != null && marker.root.activeSelf != active)
            {
                marker.root.SetActive(active);
            }
        }

        // ------------------------------------------------------------------------- build

        private Marker Resolve(int playerId)
        {
            if (_markers.TryGetValue(playerId, out Marker existing))
            {
                return existing;
            }

            Marker marker = Build(playerId);
            _markers[playerId] = marker;
            return marker;
        }

        private Marker Build(int playerId)
        {
            var root = new GameObject("Downed" + playerId);
            root.transform.SetParent(transform, false);
            var group = root.AddComponent<CanvasGroup>();

            var marker = new Marker
            {
                root = root,
                group = group
            };

            BuildPlate(marker, root.transform);
            BuildRing(marker, root.transform);

            // ⚠️ Applied to the whole marker AFTER both canvases exist: the parts are created from
            // code, so nothing inherits the processed materials of a template.
            UiDrawOnTop.Apply(root, UiDrawOnTop.HudSortingOffset);
            return marker;
        }

        private void BuildPlate(Marker marker, Transform parent)
        {
            RectTransform canvas = NewCanvas("Plate", parent, new Vector2(PlateW, PlateH));
            marker.plate = canvas;

            UiShape plate = NewShape("Bg", canvas);
            Stretch(plate.rectTransform);
            plate.Radius(Kan.PlateRadius)
                .Outline(Kan.Border1, Kan.BloodHi)
                .Fill(Kan.PlateA, Kan.PlateB, UiGradientMode.Vertical);

            var icon = NewImage("Icon", canvas);
            icon.sprite = Girdap.Icon("Warn");
            icon.color = Kan.BloodHi;
            icon.preserveAspect = true;
            icon.enabled = icon.sprite != null;
            Place(icon.rectTransform, new Vector2(48f, 48f), new Vector2(-PlateW * 0.5f + 46f, 28f));

            TMP_Text label = NewText("Label", canvas, "YARDIM", GirdapFont.RubikWetPaint, 54f,
                Kan.BoneWarm, TextAlignmentOptions.MidlineLeft);
            Place(label.rectTransform, new Vector2(220f, 60f), new Vector2(-PlateW * 0.5f + 190f, 28f));

            marker.distance = NewText("Distance", canvas, "0.0 m", GirdapFont.SairaBold, 36f,
                Kan.Label, TextAlignmentOptions.MidlineRight);
            Place(marker.distance.rectTransform, new Vector2(140f, 44f),
                new Vector2(PlateW * 0.5f - 90f, 28f));

            float trackWidth = PlateW - BarInset * 2f;
            UiShape track = NewShape("Track", canvas);
            track.Radius(BarH * 0.5f).Fill(Kan.BloodLo);
            Place(track.rectTransform, new Vector2(trackWidth, BarH), new Vector2(0f, -44f));

            // Progress is the WIDTH of a plain shape, not Image.type=Filled: a filled Image needs a
            // sprite and silently ignores fillAmount without one.
            UiShape fill = NewShape("Fill", track.transform);
            fill.Radius(BarH * 0.5f).Fill(Kan.BloodHi, Kan.Alive, UiGradientMode.Horizontal);
            fill.rectTransform.anchorMin = new Vector2(0f, 0.5f);
            fill.rectTransform.anchorMax = new Vector2(0f, 0.5f);
            fill.rectTransform.pivot = new Vector2(0f, 0.5f);
            fill.rectTransform.anchoredPosition = Vector2.zero;
            fill.rectTransform.sizeDelta = new Vector2(0f, BarH);

            marker.fill = fill.rectTransform;
            marker.trackWidth = trackWidth;
        }

        private void BuildRing(Marker marker, Transform parent)
        {
            RectTransform canvas = NewCanvas("Ring", parent, new Vector2(RingPixels, RingPixels));
            canvas.localScale = Vector3.one * (RingDiameter / RingPixels);
            marker.ring = canvas;

            UiShape ring = NewShape("Ring", canvas);
            Stretch(ring.rectTransform);

            // Clear fill + a thick outline at full corner radius IS the ring; no ring sprite needed.
            ring.Radius(RingPixels * 0.5f).Fill(Color.clear).Outline(12f, Kan.BloodHi);
        }

        /// <summary>World-space canvas sized in "pixels", scaled so one unit is a millimetre.</summary>
        private static RectTransform NewCanvas(string name, Transform parent, Vector2 size)
        {
            // The RectTransform is created UP FRONT: adding a Canvas to a plain Transform swaps the
            // component out and a cached Transform reference no longer points at it.
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.sizeDelta = size;
            rect.localScale = Vector3.one / UnitsPerMeter;

            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            return rect;
        }

        private static UiShape NewShape(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var shape = go.AddComponent<UiShape>();
            shape.raycastTarget = false;
            return shape;
        }

        private static Image NewImage(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var image = go.AddComponent<Image>();
            image.raycastTarget = false;
            return image;
        }

        private static TMP_Text NewText(string name, Transform parent, string text, GirdapFont font,
            float size, Color color, TextAlignmentOptions align)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var tmp = go.AddComponent<TextMeshProUGUI>();
            TMP_FontAsset asset = Girdap.Font(font);
            if (asset != null)
            {
                tmp.font = asset;
            }

            tmp.text = text;
            tmp.fontSize = size;
            tmp.color = color;
            tmp.alignment = align;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.raycastTarget = false;
            return tmp;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static void Place(RectTransform rect, Vector2 size, Vector2 center)
        {
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = center;
        }
    }
}
