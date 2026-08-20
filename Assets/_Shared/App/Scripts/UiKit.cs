using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

// Factory methods share element names (UiKit.Image / UiKit.Button). Aliases are required because
// `Image x = Image(...)` gives CS0119 (simple name lookup finds the member group BEFORE the type).
// Compile-time only — public signatures unchanged.
using UiImage = UnityEngine.UI.Image;
using UiButton = UnityEngine.UI.Button;

namespace VortexArena.App
{
    /// <summary>
    /// Procedural UI kit: palette + rounded sprite cache + element factories.
    /// <para>
    /// <b>Why procedural:</b> desktop/VR overlays here (connection error screen, admin spectator
    /// HUD) bootstrap themselves in EVERY scene — prefab-bound ones would need a manual step per
    /// new arena scene and would eventually be forgotten.
    /// </para>
    /// <para>
    /// <b>Layout rule:</b> no Layout Group / ContentSizeFitter. Every element sits on fixed anchors
    /// via <see cref="Block"/>/<see cref="Corner"/>/<see cref="Stretch"/>: predictable, no reflow,
    /// no drift across resolutions (scaling lives in CanvasScaler).
    /// </para>
    /// <para>
    /// <b>Font is NOT assigned:</b> TMP Settings default is used (Turkish glyphs live there).
    /// Symbols not guaranteed by that font (⚠, →, •) are never written — missing glyph renders □.
    /// </para>
    /// </summary>
    public static class UiKit
    {
        // ---------------------------------------------------------------- palette

        public static readonly Color Scrim = Hex(0x12151C, 0xDD);
        public static readonly Color Card = Hex(0x1B2029, 0xFF);

        /// <summary>In-scene panel/card: live view behind stays VISIBLE (alpha ≈ 0.88).</summary>
        public static readonly Color CardTranslucent = Hex(0x1B2029, 0xE0);

        public static readonly Color Border = Hex(0x2E3542, 0xFF);
        public static readonly Color Accent = Hex(0xF2A33C, 0xFF);
        public static readonly Color Title = Hex(0xF5F7FA, 0xFF);
        public static readonly Color Muted = Hex(0x9AA4B2, 0xFF);
        public static readonly Color Faint = Hex(0x6E7A8A, 0xFF);
        public static readonly Color OnAccent = Hex(0x12151C, 0xFF);
        public static readonly Color Good = Hex(0x39D98A, 0xFF);
        public static readonly Color Bad = Hex(0xE5484D, 0xFF);

        /// <summary>Team colors must match <see cref="Core.Player.RemoteAvatar"/> EXACTLY — a player
        /// shown in different colors on HUD vs scene misleads the operator.</summary>
        public static readonly Color TeamRed = new Color(0.85f, 0.20f, 0.20f);
        public static readonly Color TeamBlue = new Color(0.20f, 0.40f, 0.90f);
        public static readonly Color TeamNeutral = new Color(0.6f, 0.6f, 0.6f);

        /// <summary>Default panel/card corner radius (px).</summary>
        public const float PanelRadius = 12f;

        /// <summary>Card border width (px) — border color is the backdrop, fill is inset into it.</summary>
        public const float BorderWidth = 2f;

        public static Color Hex(int rgb, int alpha)
        {
            return new Color32((byte)((rgb >> 16) & 0xFF), (byte)((rgb >> 8) & 0xFF),
                (byte)(rgb & 0xFF), (byte)alpha);
        }

        /// <summary>Maps a team key ("red"/"blue"/other) to a color.</summary>
        public static Color TeamColor(string team)
        {
            return team == "red" ? TeamRed : team == "blue" ? TeamBlue : TeamNeutral;
        }

        /// <summary>Dims a color (dead player look; alpha preserved).</summary>
        public static Color Dim(Color color, float scale)
        {
            return new Color(color.r * scale, color.g * scale, color.b * scale, color.a);
        }

        public static Color WithAlpha(Color color, float alpha)
        {
            color.a = alpha;
            return color;
        }

        // ------------------------------------------------------------ canvas/root

        /// <summary>
        /// Screen-space overlay canvas (1920x1080 reference, <c>Expand</c> match: UI is designed for
        /// 16:9 — same rule as <c>AdminHud.prefab</c>'s canvas — and letterboxes instead of cropping
        /// on other aspects).
        /// <paramref name="sortingOrder"/> is cross-screen priority: admin HUD 4000,
        /// connection error screen 5000 (errors must always stay on top).
        /// </summary>
        public static Canvas ScreenCanvas(GameObject root, int sortingOrder)
        {
            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;

            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;

            root.AddComponent<GraphicRaycaster>();
            return canvas;
        }

        /// <summary>Empty node with a RectTransform (grouping/positioning).</summary>
        public static RectTransform Node(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go.AddComponent<RectTransform>();
        }

        // --------------------------------------------------------------- elements

        public static UiImage Image(Transform parent, string name, Sprite sprite, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);

            var image = go.AddComponent<UiImage>();
            image.color = color;
            image.raycastTarget = false; // clickable elements turn this on themselves

            if (sprite != null)
            {
                image.sprite = sprite;
                image.type = UiImage.Type.Sliced; // 9-slice: corners do not scale
            }

            return image;
        }

        public static TextMeshProUGUI Text(Transform parent, string name, float fontSize,
            Color color, FontStyles style, TextAlignmentOptions alignment)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);

            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.fontSize = fontSize;
            tmp.color = color;
            tmp.fontStyle = style;
            tmp.alignment = alignment;
            tmp.raycastTarget = false;
            tmp.richText = false; // external names containing "<b>" must not alter formatting
            tmp.text = "";

            return tmp;
        }

        /// <summary>
        /// Rounded card with border: border-colored backdrop + inset fill.
        /// Returned <see cref="Image"/> is the FILL — children are parented to it.
        /// </summary>
        public static UiImage Panel(Transform parent, string name, Color fill, Color border,
            float radius = PanelRadius)
        {
            UiImage outer = Image(parent, name, RoundedSprite(radius), border);
            UiImage inner = Image(outer.rectTransform, "Fill", RoundedSprite(radius), fill);
            Stretch(inner.rectTransform, BorderWidth);
            return inner;
        }

        /// <summary>Borderless flat surface (separator, strip, bar backdrop).</summary>
        public static UiImage Solid(Transform parent, string name, Color color, bool rounded = false)
        {
            return Image(parent, name, rounded ? RoundedSprite(4f) : null, color);
        }

        /// <summary>
        /// Button: rounded backdrop + centered label. `background.color` is the base color,
        /// Button.colors only tints (normal = white).
        /// </summary>
        public static UiButton Button(Transform parent, string name, string label, float fontSize,
            Color background, Color foreground, UnityAction onClick, out TextMeshProUGUI labelText)
        {
            UiImage image = Image(parent, name, RoundedSprite(8f), background);
            image.raycastTarget = true;

            labelText = Text(image.rectTransform, "Label", fontSize, foreground, FontStyles.Bold,
                TextAlignmentOptions.Center);
            Stretch(labelText.rectTransform, 2f);
            labelText.text = label;
            // NoWrap + Ellipsis: long labels do not break the button. (`enableWordWrapping` obsolete.)
            labelText.textWrappingMode = TextWrappingModes.NoWrap;
            labelText.overflowMode = TextOverflowModes.Ellipsis;
            // Safety net: an oversized label shrinks BEFORE being clipped. Scales down only
            // (max = requested size), so fitting labels are untouched. Needed because button widths
            // come from fixed anchors (no Layout Group) and label length varies — hand-computing
            // "how many px fit" per call site is error-prone. Floor at 70%: below that it becomes
            // unreadable and ellipsis is preferable.
            labelText.enableAutoSizing = true;
            labelText.fontSizeMax = fontSize;
            labelText.fontSizeMin = Mathf.Max(8f, fontSize * 0.7f);

            var button = image.gameObject.AddComponent<UiButton>();
            button.targetGraphic = image;

            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.15f, 1.15f, 1.15f, 1f);
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            colors.selectedColor = Color.white;
            colors.disabledColor = new Color(0.42f, 0.45f, 0.52f, 1f);
            colors.fadeDuration = 0.08f;
            button.colors = colors;

            if (onClick != null)
            {
                button.onClick.AddListener(onClick);
            }

            return button;
        }

        /// <summary>
        /// Single-line text field: dark rounded backdrop + <see cref="TMP_InputField"/>.
        /// <para>
        /// ⚠️ <b>The kit's ONLY keyboard field, DESKTOP (admin) UI only.</b> VR needs the system
        /// keyboard; text input there is a separate solution.
        /// </para>
        /// <para>
        /// Component is added while the object is <b>inactive</b>: <c>TMP_InputField.OnEnable</c>
        /// looks for <c>textComponent</c>/<c>textViewport</c> on activation — added to an active
        /// object it wakes half-wired with a broken caret. Order: deactivate → add → wire → activate.
        /// </para>
        /// </summary>
        public static TMP_InputField Input(Transform parent, string name, string placeholder,
            float fontSize, int characterLimit, UnityAction<string> onEndEdit)
        {
            UiImage background = Image(parent, name, RoundedSprite(8f), Hex(0x12151C, 0xFF));
            background.raycastTarget = true;

            RectTransform viewport = Node(background.rectTransform, "TextArea");
            Stretch(viewport, 8f);
            viewport.gameObject.AddComponent<RectMask2D>();

            TextMeshProUGUI text = Text(viewport, "Text", fontSize, Title, FontStyles.Normal,
                TextAlignmentOptions.MidlineLeft);
            Stretch(text.rectTransform);

            TextMeshProUGUI hint = Text(viewport, "Placeholder", fontSize, Faint, FontStyles.Italic,
                TextAlignmentOptions.MidlineLeft);
            Stretch(hint.rectTransform);
            hint.text = placeholder;

            GameObject host = background.gameObject;
            host.SetActive(false);

            var field = host.AddComponent<TMP_InputField>();
            field.targetGraphic = background;
            field.textViewport = viewport;
            field.textComponent = text;
            field.placeholder = hint;
            field.characterLimit = characterLimit;
            field.lineType = TMP_InputField.LineType.SingleLine;
            field.richText = false;
            field.restoreOriginalTextOnEscape = true;
            field.text = "";

            if (onEndEdit != null)
            {
                field.onEndEdit.AddListener(onEndEdit);
            }

            host.SetActive(true);
            return field;
        }

        /// <summary>
        /// Horizontal fill bar (HP/progress): backdrop + fill growing from the left.
        /// Fill width is set 0..1 via <c>fill.rectTransform.anchorMax.x</c>.
        /// </summary>
        public static UiImage Bar(Transform parent, string name, Color background, Color fill)
        {
            UiImage back = Image(parent, name, RoundedSprite(4f), background);

            UiImage front = Image(back.rectTransform, "Fill", RoundedSprite(4f), fill);
            RectTransform rect = front.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            return front;
        }

        /// <summary>Sets bar fill in the 0..1 range.</summary>
        public static void SetBarFill(UiImage fill, float normalized)
        {
            if (fill == null)
            {
                return;
            }

            RectTransform rect = fill.rectTransform;
            rect.anchorMax = new Vector2(Mathf.Clamp01(normalized), 1f);
        }

        // ---------------------------------------------------------------- layout

        /// <summary>Fills the parent completely (optional padding).</summary>
        public static void Stretch(RectTransform rect, float padding = 0f)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = new Vector2(padding, padding);
            rect.offsetMax = new Vector2(-padding, -padding);
        }

        /// <summary>Top-anchored, horizontally stretched block (basis of row layout).</summary>
        public static void Block(RectTransform rect, float left, float top, float right, float height)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.offsetMin = new Vector2(left, -(top + height));
            rect.offsetMax = new Vector2(-right, -top);
        }

        /// <summary>
        /// Box pinned to a corner/edge. <paramref name="anchor"/> doubles as pivot,
        /// <paramref name="offset"/> shifts inward from that corner (sign relative to the anchor).
        /// E.g. bottom-right: anchor (1,0), offset (-24, 24).
        /// </summary>
        public static void Corner(RectTransform rect, Vector2 anchor, Vector2 offset, Vector2 size)
        {
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = anchor;
            rect.anchoredPosition = offset;
            rect.sizeDelta = size;
        }

        /// <summary>Fixed-size box centered in the parent.</summary>
        public static void Center(RectTransform rect, Vector2 size, Vector2 offset = default)
        {
            Corner(rect, new Vector2(0.5f, 0.5f), offset, size);
        }

        // --------------------------------------------------------- EventSystem

        private static EventSystem _ownEventSystem;

        /// <summary>
        /// Guarantees an EventSystem for clickable desktop UI.
        /// <para>
        /// ⚠ <b>Trap:</b> arena scenes have NO EventSystem (only Lobby does) — HUD buttons would
        /// silently die once the admin spectator entered an arena scene. Hence a persistent
        /// (DontDestroyOnLoad) one.
        /// </para>
        /// <para>
        /// ⚠ Second trap: two active EventSystems make Unity log "There are 2 event systems" and
        /// split input, so <see cref="TakeOverEventSystem"/> disables the scene-owned ones.
        /// </para>
        /// Project builds with "Input System Package (New)", so the module is
        /// <see cref="InputSystemUIInputModule"/>; legacy <c>StandaloneInputModule</c> touches
        /// <c>UnityEngine.Input</c> at runtime and throws.
        /// </summary>
        public static EventSystem EnsureEventSystem()
        {
            if (_ownEventSystem != null)
            {
                return _ownEventSystem;
            }

            // EventSystem.current can be null before the scene object's OnEnable — confirm by search.
            EventSystem existing = EventSystem.current != null
                ? EventSystem.current
                : Object.FindFirstObjectByType<EventSystem>();
            if (existing != null && existing.isActiveAndEnabled)
            {
                return existing; // scene's own EventSystem is doing the job
            }

            return CreateOwnEventSystem();
        }

        /// <summary>
        /// Makes the persistent EventSystem the sole authority by disabling scene-owned ones.
        /// Called only by the admin spectator — Lobby's EventSystem exists for the VR pointer and
        /// is useless on desktop admin.
        /// </summary>
        public static void TakeOverEventSystem()
        {
            if (_ownEventSystem == null)
            {
                CreateOwnEventSystem();
            }

            EventSystem[] all = Object.FindObjectsByType<EventSystem>(FindObjectsSortMode.None);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null && all[i] != _ownEventSystem)
                {
                    all[i].gameObject.SetActive(false);
                }
            }

            EventSystem.current = _ownEventSystem;
        }

        private static EventSystem CreateOwnEventSystem()
        {
            var go = new GameObject("[VortexEventSystem]");
            Object.DontDestroyOnLoad(go);
            _ownEventSystem = go.AddComponent<EventSystem>();
            go.AddComponent<InputSystemUIInputModule>();
            return _ownEventSystem;
        }

        // ---------------------------------------------- procedural rounded corner

        // One sprite per radius (key in px); texture fixed at 64x64.
        private static readonly Dictionary<int, Sprite> RoundedCache = new Dictionary<int, Sprite>();

        /// <summary>
        /// 64x64 white rounded-rect alpha mask with anti-aliased edges. Built with a 9-slice
        /// `border` → <c>Image.Type.Sliced</c> keeps corners intact at any size. Color comes from
        /// <c>Image.color</c>; sprites are cached per radius.
        /// </summary>
        public static Sprite RoundedSprite(float radius = PanelRadius)
        {
            const int size = 64;
            const float half = size * 0.5f;

            int key = Mathf.Clamp(Mathf.RoundToInt(radius), 1, (int)half - 1);
            if (RoundedCache.TryGetValue(key, out Sprite cached) && cached != null)
            {
                return cached;
            }

            float r = key;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = $"VortexRoundedRect{key}",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    // Rounded-rect signed distance field (negative inside).
                    float qx = Mathf.Abs(x + 0.5f - half) - (half - r);
                    float qy = Mathf.Abs(y + 0.5f - half) - (half - r);
                    float outside = Mathf.Sqrt(Mathf.Max(qx, 0f) * Mathf.Max(qx, 0f) +
                                               Mathf.Max(qy, 0f) * Mathf.Max(qy, 0f));
                    float distance = Mathf.Min(Mathf.Max(qx, qy), 0f) + outside - r;

                    // 1 px soft edge (anti-aliasing).
                    byte alpha = (byte)Mathf.RoundToInt(Mathf.Clamp01(0.5f - distance) * 255f);
                    pixels[y * size + x] = new Color32(255, 255, 255, alpha);
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            texture.hideFlags = HideFlags.DontSave; // runtime-generated — never written as an asset

            // 9-slice border is 2 px larger than the radius so the corner curve fits the slice.
            float b = Mathf.Min(half - 1f, r + 2f);
            Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, size, size),
                new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect,
                new Vector4(b, b, b, b));
            sprite.name = $"VortexRoundedRectSprite{key}";
            sprite.hideFlags = HideFlags.DontSave;

            RoundedCache[key] = sprite;
            return sprite;
        }

        // One ring sprite per thickness (key in percent).
        private static readonly Dictionary<int, Sprite> RingCache = new Dictionary<int, Sprite>();

        /// <summary>
        /// Annulus alpha mask — admin spectator's player rings.
        /// <paramref name="thickness"/> is stroke width relative to radius (0.05..0.5).
        /// <para>
        /// UI sprite instead of mesh + <c>Shader.Find</c>: unused shaders can be STRIPPED from the
        /// build and <c>Shader.Find("Universal Render Pipeline/Unlit")</c> would return null.
        /// UI/TMP shaders are always in the build, so a world-space canvas is the safe path.
        /// </para>
        /// </summary>
        public static Sprite RingSprite(float thickness = 0.16f)
        {
            const int size = 128;
            const float half = size * 0.5f;

            int key = Mathf.Clamp(Mathf.RoundToInt(thickness * 100f), 5, 50);
            if (RingCache.TryGetValue(key, out Sprite cached) && cached != null)
            {
                return cached;
            }

            float outer = half - 1f;
            float inner = outer * (1f - key / 100f);

            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = $"VortexRing{key}",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x + 0.5f - half;
                    float dy = y + 0.5f - half;
                    float distance = Mathf.Sqrt(dx * dx + dy * dy);

                    // 1 px soft transition on both edges (anti-aliasing).
                    float a = Mathf.Clamp01(outer + 0.5f - distance) *
                              Mathf.Clamp01(distance - inner + 0.5f);
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            texture.hideFlags = HideFlags.DontSave;

            // Ring SCALES (not 9-sliced): border 0 → used with Image.Type.Simple.
            Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, size, size),
                new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
            sprite.name = $"VortexRingSprite{key}";
            sprite.hideFlags = HideFlags.DontSave;

            RingCache[key] = sprite;
            return sprite;
        }

        /// <summary>
        /// World-space canvas root (admin rings/name tags). No camera assignment needed for
        /// rendering; no <c>GraphicRaycaster</c> either since nothing raycasts against it.
        /// </summary>
        public static Canvas WorldCanvas(Transform parent, string name, Vector2 size, float scale)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);

            // RectTransform FIRST (upgrades the Transform): Canvas depends on it.
            var rect = go.AddComponent<RectTransform>();
            rect.sizeDelta = size;
            rect.localScale = Vector3.one * scale;

            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;

            return canvas;
        }
    }
}
