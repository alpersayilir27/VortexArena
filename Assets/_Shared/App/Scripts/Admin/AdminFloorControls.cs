using System.Collections.Generic;
using TMPro;
using UnityEngine;
using VortexArena.Core.Arena;
using VortexArena.Core.UI;

namespace VortexArena.App.Admin
{
    /// <summary>
    /// Floor selector at the right end of the HUD's match bar: picks which floor the operator watches
    /// (<see cref="AdminSession.Floor"/>).
    /// <para>⚠️ <b>Visible only in a multi-floor arena</b>, and the bar SHRINKS back when it is not:
    /// in a single-floor arena the strip ends at İPTAL (index.html says so in words). The floor count
    /// is a property of the loaded scene, so the buttons cannot live in the prefab — they are cloned
    /// from an inactive template.</para>
    /// <para>The top-down camera clips to the chosen floor (<see cref="AdminSpectatorCamera"/>),
    /// which is why picking one also switches the camera mode.</para>
    /// </summary>
    public class AdminFloorControls : MonoBehaviour
    {
        /// <summary>CSS <c>.mbar .seg .btn { min-width: 92px }</c>.</summary>
        private const float MinButtonWidth = 92f;

        /// <summary>CSS <c>.btn { padding: 0 16px }</c> on both sides.</summary>
        private const float ButtonPadding = 32f;

        /// <summary>CSS <c>.seg { gap: 4px }</c>.</summary>
        private const float ButtonGap = 4f;

        [Tooltip("Maç şeridinin kökü; kat grubu açılınca genişler, kapanınca İPTAL'de biter.")]
        [SerializeField] private RectTransform matchBar;

        [Tooltip("Şeridin kat grubu OLMADAN genişliği (px) — builder yazar.")]
        [SerializeField] private float baseWidth;

        [Tooltip("Ayırıcı + \"KAT\" etiketi + seçici; tek katlı arenada tamamen kapanır.")]
        [SerializeField] private RectTransform floorGroup;

        [Tooltip("Kat düğmelerinin konduğu kutu; genişliği kat sayısından hesaplanır.")]
        [SerializeField] private RectTransform floorSegment;

        [Tooltip("Kat düğmesi şablonu — PASİF durur, her kat için bir kopya çıkar.")]
        [SerializeField] private UiButtonStyle buttonTemplate;

        private readonly List<UiButtonStyle> _buttons = new List<UiButtonStyle>();

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

        private void Update()
        {
            if (matchBar == null || floorGroup == null)
            {
                return;
            }

            if (floorSegment == null || buttonTemplate == null)
            {
                Debug.LogWarning("[AdminFloorControls] Kat seçici referansları eksik; kat şeridi çizilmedi.");
                enabled = false;
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

            for (int i = 0; i < _buttons.Count; i++)
            {
                if (_buttons[i] != null)
                {
                    Destroy(_buttons[i].gameObject);
                }
            }

            _buttons.Clear();

            // A shrinking arena must not leave the selection pointing at a floor that no longer exists.
            AdminSession.Floor = Mathf.Clamp(AdminSession.Floor, 0, Mathf.Max(0, count - 1));

            bool show = count > 1;
            floorGroup.gameObject.SetActive(show);
            if (!show)
            {
                SetBarWidth(0f);
                return;
            }

            float x = 0f;
            for (int floor = 0; floor < count; floor++)
            {
                int index = floor;
                UiButtonStyle button = Instantiate(buttonTemplate, floorSegment);
                button.gameObject.SetActive(true);
                button.name = $"Floor{floor}";
                button.SetLabel(FloorLabel(floor));

                float width = ButtonWidth(button);
                var rect = (RectTransform)button.transform;
                rect.anchoredPosition = new Vector2(x, 0f);
                rect.sizeDelta = new Vector2(width, rect.sizeDelta.y);
                CenterLabel(button, width);

                if (button.TargetButton != null)
                {
                    button.TargetButton.onClick.RemoveAllListeners();
                    button.TargetButton.onClick.AddListener(() => Select(index));
                }

                _buttons.Add(button);
                x += width + ButtonGap;
            }

            float segmentWidth = Mathf.Max(0f, x - ButtonGap);
            floorSegment.sizeDelta = new Vector2(segmentWidth, floorSegment.sizeDelta.y);

            // The group's own left offset already covers the separator and the "KAT" label, so the
            // selector's x inside the group IS everything that precedes it.
            float groupWidth = floorSegment.anchoredPosition.x + segmentWidth;
            floorGroup.sizeDelta = new Vector2(groupWidth, floorGroup.sizeDelta.y);
            SetBarWidth(groupWidth);

            Refresh();
        }

        /// <summary>Bar width with the floor group's contribution. The bar is center-anchored, so it
        /// grows symmetrically and the whole strip re-centers — what the mockup shows.</summary>
        private void SetBarWidth(float extra)
        {
            matchBar.sizeDelta = new Vector2(baseWidth + extra, matchBar.sizeDelta.y);
        }

        private static float ButtonWidth(UiButtonStyle button)
        {
            TMP_Text label = button.Label;
            float text = label != null ? Mathf.Ceil(label.GetPreferredValues(label.text).x) : 0f;
            return Mathf.Max(MinButtonWidth, ButtonPadding + text);
        }

        /// <summary>Centers the label in a box whose width was just computed (no Layout Group here).</summary>
        private static void CenterLabel(UiButtonStyle button, float width)
        {
            TMP_Text label = button.Label;
            if (label == null)
            {
                return;
            }

            var rect = (RectTransform)button.transform;
            float text = Mathf.Ceil(label.GetPreferredValues(label.text).x);
            label.rectTransform.anchoredPosition = new Vector2((width - text) * 0.5f, 0f);
            label.rectTransform.sizeDelta = new Vector2(text, rect.sizeDelta.y);
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
            for (int i = 0; i < _buttons.Count; i++)
            {
                if (_buttons[i] != null)
                {
                    // CSS overrides `.seg .btn` inside `.mbar` back to the normal surface, so the idle
                    // item is a plain button here — not the transparent Seg item.
                    _buttons[i].SetKind(i == selected ? UiButtonKind.On : UiButtonKind.Normal);
                }
            }
        }
    }
}
