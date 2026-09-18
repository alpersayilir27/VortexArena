using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VortexArena.Core.Arena;

namespace VortexArena.App.Admin
{
    /// <summary>
    /// Floor selector row in the HUD's match bar: picks which floor the operator watches
    /// (<see cref="AdminSession.Floor"/>).
    /// <para>Visible only when the arena has more than one floor; the top-down camera then clips to
    /// that floor (<see cref="AdminSpectatorCamera"/>).</para>
    /// <para>Built from <see cref="ArenaFloors"/> at runtime — the floor count is a property of the
    /// loaded scene, so a prefab row would be wrong in every other arena.</para>
    /// </summary>
    public class AdminFloorControls : MonoBehaviour
    {
        private const float ButtonWidth = 64f;
        private const float ButtonHeight = 40f;
        private const float ButtonGap = 6f;
        private const float BarHeight = 44f;
        private const float FontSize = 14f;

        /// <summary>Gap between the prefab's icon row and the first floor button (px).</summary>
        private const float RowGap = 24f;

        /// <summary>Idle button background, distinct from the selected <see cref="UiKit.Accent"/>.</summary>
        private static readonly Color IdleFill = UiKit.Hex(0x2A303B, 0xFF);

        private readonly List<Image> _backgrounds = new List<Image>();
        private readonly List<TextMeshProUGUI> _labels = new List<TextMeshProUGUI>();

        private RectTransform _bar;
        private int _builtVersion = -1;
        private int _builtCount = -1;

        private void OnEnable()
        {
            AdminSession.Changed += Refresh;
        }

        private void OnDisable()
        {
            AdminSession.Changed -= Refresh;
        }

        private void Start()
        {
            // The driver component marks the bar; a bare name lookup breaks on a re-parented node.
            AdminMatchControls match = GetComponentInChildren<AdminMatchControls>(true);
            Transform matchBar = match != null ? match.transform : transform.Find("MatchBar");
            if (matchBar == null)
            {
                Debug.LogWarning("[AdminFloorControls] MatchBar bulunamadı; kat seçici çizilmedi.");
                enabled = false;
                return;
            }

            _bar = UiKit.Node(matchBar, "FloorBar");
            // Grows to the RIGHT of the icon row: the bar has no visible edge to hang from, and a
            // right-anchored row would run back over the icons on a narrow bar.
            _bar.anchorMin = new Vector2(0.5f, 0.5f);
            _bar.anchorMax = new Vector2(0.5f, 0.5f);
            _bar.pivot = new Vector2(0f, 0.5f);
            _bar.anchoredPosition = new Vector2(IconRowRightEdge((RectTransform)matchBar, _bar) + RowGap, 0f);
            _bar.sizeDelta = new Vector2(0f, BarHeight);
            _bar.gameObject.SetActive(false);
        }

        /// <summary>Right edge of the prefab's icon buttons, as an offset from the bar's center (local x).</summary>
        private static float IconRowRightEdge(RectTransform matchBar, RectTransform exclude)
        {
            // World corners, so the answer does not depend on how the prefab anchors its buttons.
            var corners = new Vector3[4];
            float edge = float.NegativeInfinity;
            for (int i = 0; i < matchBar.childCount; i++)
            {
                var child = matchBar.GetChild(i) as RectTransform;
                if (child == null || child == exclude || !child.gameObject.activeSelf)
                {
                    continue;
                }

                child.GetWorldCorners(corners);
                edge = Mathf.Max(edge, matchBar.InverseTransformPoint(corners[2]).x); // top-right
            }

            return float.IsNegativeInfinity(edge) ? 0f : edge - matchBar.rect.center.x;
        }

        private void Update()
        {
            if (_bar == null)
            {
                return;
            }

            int count = ArenaFloors.Count;
            if (ArenaFloors.Version != _builtVersion || count != _builtCount)
            {
                Build(count);
            }
        }

        private void Build(int count)
        {
            _builtVersion = ArenaFloors.Version;
            _builtCount = count;

            for (int i = 0; i < _backgrounds.Count; i++)
            {
                if (_backgrounds[i] != null)
                {
                    Destroy(_backgrounds[i].gameObject);
                }
            }

            _backgrounds.Clear();
            _labels.Clear();

            // A shrinking arena must not leave the selection pointing at a floor that no longer exists.
            AdminSession.Floor = Mathf.Clamp(AdminSession.Floor, 0, Mathf.Max(0, count - 1));

            _bar.gameObject.SetActive(count > 1);
            if (count <= 1)
            {
                return;
            }

            float width = count * ButtonWidth + (count - 1) * ButtonGap;
            _bar.sizeDelta = new Vector2(width, BarHeight);

            for (int floor = 0; floor < count; floor++)
            {
                int index = floor;
                Button button = UiKit.Button(
                    _bar,
                    $"Floor{floor}",
                    FloorLabel(floor),
                    FontSize,
                    IdleFill,
                    UiKit.Muted,
                    () => Select(index),
                    out TextMeshProUGUI label);

                var rect = (RectTransform)button.transform;
                rect.anchorMin = new Vector2(0f, 0.5f);
                rect.anchorMax = new Vector2(0f, 0.5f);
                rect.pivot = new Vector2(0f, 0.5f);
                rect.sizeDelta = new Vector2(ButtonWidth, ButtonHeight);
                rect.anchoredPosition = new Vector2(floor * (ButtonWidth + ButtonGap), 0f);

                _backgrounds.Add(button.targetGraphic as Image);
                _labels.Add(label);
            }

            Refresh();
        }

        /// <summary>Ground floor is named, the rest numbered — "0. kat" means nothing to an operator.</summary>
        private static string FloorLabel(int floor)
        {
            return floor == 0 ? "Zemin" : $"{floor}. kat";
        }

        private static void Select(int floor)
        {
            AdminSession.Floor = floor;
            // Picking a floor is a request to SEE it, and only top-down clips to one.
            AdminSession.CameraMode = AdminCameraMode.TopDown;
        }

        private void Refresh()
        {
            int selected = AdminSession.Floor;
            for (int i = 0; i < _backgrounds.Count; i++)
            {
                bool active = i == selected;

                if (_backgrounds[i] != null)
                {
                    _backgrounds[i].color = active ? UiKit.Accent : IdleFill;
                }

                if (_labels[i] != null)
                {
                    _labels[i].color = active ? UiKit.OnAccent : UiKit.Muted;
                }
            }
        }
    }
}
