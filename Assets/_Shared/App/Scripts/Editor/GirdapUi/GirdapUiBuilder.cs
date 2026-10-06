using TMPro;
using UnityEditor;
using UnityEngine;
using VortexArena.Core.UI;

// Factory methods share element names (Image / Button / Text). Aliases are required because
// `Image x = Image(...)` gives CS0119 — simple name lookup finds the member group BEFORE the type.
using UiImage = UnityEngine.UI.Image;
using UiButton = UnityEngine.UI.Button;

namespace VortexArena.App.Editor
{
    /// <summary>
    /// Generates the "Girdap" theme's prefabs from code.
    /// <para>
    /// <b>Why generated:</b> the mockups in <c>plan/arayuz-yenileme/</c> are the single source of
    /// truth and carry exact px values. Hand-placing ~hundreds of rects in the Inspector drifts from
    /// them silently; here a CSS change is a diff in one method.
    /// </para>
    /// <para>
    /// ⚠️ <b>Layout rule (same as <c>UiKit</c>):</b> NO Layout Group / ContentSizeFitter. Everything
    /// sits on fixed anchors — predictable, no reflow, no drift across resolutions.
    /// </para>
    /// Screen-specific parts live in sibling partials (<c>GirdapUiBuilder.&lt;Screen&gt;.cs</c>).
    /// </summary>
    public static partial class GirdapUiBuilder
    {
        public const string PrefabDir = "Assets/_Shared/App/Resources/UI/";
        public const string IconDir = "Assets/_Shared/App/UI/Sprites/";
        public const string FontDir = "Assets/_Shared/App/UI/Fonts/";
        public const string FontAssetDir = "Assets/_Shared/App/Resources/UI/Fonts/";

        // Screen builders live in other partials; an unimplemented partial method call is dropped by
        // the compiler, so this file compiles on its own.
        static partial void BuildPlayerRow();

        static partial void BuildStatsRow();

        static partial void BuildStatsPanel();

        static partial void BuildPreferencesPanel();

        static partial void BuildHud();

        static partial void BuildMatchResult();

        [MenuItem("Tools/VortexArena/UI/Girdap/Tüm prefabları üret")]
        public static void BuildAll()
        {
            BuildAssets();
            BuildPlayerRow();
            BuildStatsRow();
            BuildStatsPanel();
            BuildPreferencesPanel();
            BuildHud();
            BuildMatchResult();
            AssetDatabase.SaveAssets();
        }

        // ---------------------------------------------------------------- layout
        // CSS mindset: x/y measured from the PARENT'S TOP-LEFT corner, plus w/h.

        public static RectTransform Node(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go.AddComponent<RectTransform>();
        }

        public static void Place(RectTransform rt, float x, float y, float w, float h)
        {
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(w, h);
        }

        public static void PlaceRight(RectTransform rt, float right, float y, float w, float h)
        {
            rt.anchorMin = new Vector2(1f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 1f);
            rt.anchoredPosition = new Vector2(-right, -y);
            rt.sizeDelta = new Vector2(w, h);
        }

        public static void PlaceTopCenter(RectTransform rt, float y, float w, float h)
        {
            rt.anchorMin = new Vector2(0.5f, 1f);
            rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, -y);
            rt.sizeDelta = new Vector2(w, h);
        }

        public static void PlaceBottomCenter(RectTransform rt, float bottom, float w, float h)
        {
            rt.anchorMin = new Vector2(0.5f, 0f);
            rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.anchoredPosition = new Vector2(0f, bottom);
            rt.sizeDelta = new Vector2(w, h);
        }

        public static void PlaceBottomRight(RectTransform rt, float right, float bottom, float w,
            float h)
        {
            rt.anchorMin = new Vector2(1f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(1f, 0f);
            rt.anchoredPosition = new Vector2(-right, bottom);
            rt.sizeDelta = new Vector2(w, h);
        }

        public static void PlaceBottomLeft(RectTransform rt, float left, float bottom, float w,
            float h)
        {
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(0f, 0f);
            rt.pivot = new Vector2(0f, 0f);
            rt.anchoredPosition = new Vector2(left, bottom);
            rt.sizeDelta = new Vector2(w, h);
        }

        /// <summary>Centre-anchored box; <paramref name="dx"/>/<paramref name="dy"/> are CSS-style
        /// offsets from the parent's centre (y down). ⚠️ Full-screen panel cards use this, not
        /// <see cref="Place"/>: CanvasScaler Expand gives a non-16:9 window extra canvas width or
        /// height, and a top-left-placed card drifts off centre.</summary>
        public static void PlaceCenter(RectTransform rt, float dx, float dy, float w, float h)
        {
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(dx, -dy);
            rt.sizeDelta = new Vector2(w, h);
        }

        /// <summary>Vertically centered, left-anchored box (inline row content).</summary>
        public static void PlaceMiddleLeft(RectTransform rt, float x, float w, float h)
        {
            rt.anchorMin = new Vector2(0f, 0.5f);
            rt.anchorMax = new Vector2(0f, 0.5f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.anchoredPosition = new Vector2(x, 0f);
            rt.sizeDelta = new Vector2(w, h);
        }

        /// <summary>Fills the parent with per-edge insets.</summary>
        public static void Stretch(RectTransform rt, float l = 0f, float t = 0f, float r = 0f,
            float b = 0f)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(l, b);
            rt.offsetMax = new Vector2(-r, -t);
        }

        /// <summary>Full width, fixed height, pinned <paramref name="t"/> px from the top.</summary>
        public static void StretchTop(RectTransform rt, float l, float t, float r, float h)
        {
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.offsetMin = new Vector2(l, -(t + h));
            rt.offsetMax = new Vector2(-r, -t);
        }

        /// <summary>Full width, fixed height, pinned <paramref name="b"/> px from the bottom.</summary>
        public static void StretchBottom(RectTransform rt, float l, float b, float r, float h)
        {
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.offsetMin = new Vector2(l, b);
            rt.offsetMax = new Vector2(-r, b + h);
        }

        /// <summary>Full height, fixed width, pinned <paramref name="x"/> px from the left.</summary>
        public static void StretchLeft(RectTransform rt, float x, float t, float w, float b)
        {
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.offsetMin = new Vector2(x, b);
            rt.offsetMax = new Vector2(x + w, -t);
        }

        // -------------------------------------------------------------- graphics

        public static UiShape Shape(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var shape = go.AddComponent<UiShape>();
            shape.raycastTarget = false; // clickable parts turn this on themselves
            return shape;
        }

        public static UiStripes Stripes(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var stripes = go.AddComponent<UiStripes>();
            stripes.raycastTarget = false;
            return stripes;
        }

        public static UiSegmentBar SegmentBar(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var bar = go.AddComponent<UiSegmentBar>();
            bar.raycastTarget = false;
            return bar;
        }

        public static UiImage Image(Transform parent, string name, Sprite sprite, Color color,
            UiImage.Type type = UiImage.Type.Simple)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);

            var image = go.AddComponent<UiImage>();
            image.color = color;
            image.raycastTarget = false;
            if (sprite != null)
            {
                image.sprite = sprite;
                image.type = type;
            }

            return image;
        }

        /// <summary>
        /// Theme icon by bare name ("Skull" → <c>Ic_Skull.png</c>). Loaded through AssetDatabase, not
        /// <see cref="Girdap.Icon"/>: the container may not exist yet when the prefabs are generated.
        /// </summary>
        public static UiImage Icon(Transform parent, string name, string icon, float size, Color color)
        {
            Sprite sprite = LoadIcon(icon);
            UiImage image = Image(parent, name, sprite, color);
            image.preserveAspect = true;
            image.rectTransform.sizeDelta = new Vector2(size, size);
            return image;
        }

        public static Sprite LoadIcon(string icon)
        {
            if (string.IsNullOrEmpty(icon))
            {
                return null;
            }

            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(IconDir + "Ic_" + icon + ".png");
            if (sprite == null)
            {
                Debug.LogWarning($"[Girdap] Ic_{icon}.png bulunamadı: {IconDir}");
            }

            return sprite;
        }

        public static Sprite LoadSprite(string fileName)
        {
            return AssetDatabase.LoadAssetAtPath<Sprite>(IconDir + fileName);
        }

        public static TextMeshProUGUI Text(Transform parent, string name, string text,
            GirdapFont font, float size, Color color, TextAlignmentOptions align,
            float letterSpacingEm = 0f, bool upper = false, FontStyles style = FontStyles.Normal)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);

            var tmp = go.AddComponent<TextMeshProUGUI>();
            TMP_FontAsset asset = LoadFontAsset(font);
            if (asset != null)
            {
                tmp.font = asset;
            }

            tmp.text = text ?? "";
            tmp.fontSize = size;
            tmp.color = color;
            tmp.alignment = align;
            tmp.characterSpacing = Girdap.Spacing(letterSpacingEm);
            tmp.fontStyle = upper ? style | FontStyles.UpperCase : style;
            tmp.richText = false; // player names containing "<b>" must not alter formatting
            tmp.textWrappingMode = TextWrappingModes.NoWrap; // `enableWordWrapping` is obsolete in uGUI 2.0
            tmp.overflowMode = TextOverflowModes.Overflow;
            tmp.enableAutoSizing = false; // every size comes from the mockup; autosize would hide drift
            tmp.raycastTarget = false;
            return tmp;
        }

        /// <summary>
        /// CSS <c>text-shadow: 0 0 Npx C</c> / <c>drop-shadow</c> on text: a TMP material preset
        /// with Underlay on, one asset per font + colour. ⚠️ Must be an ASSET, not a runtime
        /// keyword: <c>UNDERLAY_ON</c> is a shader_feature and the build strips the variant unless
        /// a material in the build uses it. Blur is capped by the atlas padding, so a 48 px clock
        /// gets a few px of halo where CSS gives 24.
        /// </summary>
        public static void TextGlow(TextMeshProUGUI tmp, Color color, float softness = 1f,
            float dilate = 0.3f)
        {
            if (tmp == null || tmp.font == null || tmp.font.material == null)
            {
                return;
            }

            Material baseMaterial = tmp.font.material;
            string path = FontAssetDir + tmp.font.name + " Glow " + ColorUtility.ToHtmlStringRGBA(color)
                          + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(baseMaterial);
                AssetDatabase.CreateAsset(mat, path);
            }

            mat.shader = baseMaterial.shader;
            mat.SetTexture(ShaderUtilities.ID_MainTex, baseMaterial.GetTexture(ShaderUtilities.ID_MainTex));
            mat.EnableKeyword(ShaderUtilities.Keyword_Underlay);
            mat.SetColor(ShaderUtilities.ID_UnderlayColor, color);
            mat.SetFloat(ShaderUtilities.ID_UnderlayOffsetX, 0f);
            mat.SetFloat(ShaderUtilities.ID_UnderlayOffsetY, 0f);
            mat.SetFloat(ShaderUtilities.ID_UnderlayDilate, dilate);
            mat.SetFloat(ShaderUtilities.ID_UnderlaySoftness, softness);
            EditorUtility.SetDirty(mat);
            tmp.fontSharedMaterial = mat;
        }

        public static TMP_FontAsset LoadFontAsset(GirdapFont font)
        {
            string path = FontAssetDir + FontFileName(font) + " SDF.asset";
            var asset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
            if (asset == null)
            {
                Debug.LogError($"[Girdap] {path} yok — önce \"Fontları ve asset kabını üret\"i çalıştır.");
            }

            return asset;
        }

        /// <summary>TTF/SDF file name stem of a theme font role.</summary>
        public static string FontFileName(GirdapFont font)
        {
            switch (font)
            {
                case GirdapFont.ChakraBold: return "ChakraPetch-Bold";
                case GirdapFont.ChakraSemiBold: return "ChakraPetch-SemiBold";
                case GirdapFont.SairaExtraBold: return "SairaCondensed-ExtraBold";
                case GirdapFont.SairaBold: return "SairaCondensed-Bold";
                case GirdapFont.SairaSemiBold: return "SairaCondensed-SemiBold";
                case GirdapFont.BarlowMedium: return "Barlow-Medium";
                default: return "ChakraPetch-Bold";
            }
        }

        // ------------------------------------------------------------- composites

        /// <summary>Chamfered, bordered box with a vertical fill gradient (the theme's base surface).</summary>
        public static UiShape ShapeBox(Transform parent, string name, float x, float y, float w,
            float h, float chamfer, Color bdA, Color bdB, Color bgA, Color bgB, float bw = 1f)
        {
            UiShape shape = Shape(parent, name);
            Place(shape.rectTransform, x, y, w, h);
            shape.Chamfer(chamfer)
                .Outline(bw, bdA, bdB)
                .Fill(bgA, bgB, UiGradientMode.Vertical);
            return shape;
        }

        /// <summary>
        /// Keycap (CSS <c>kbd</c>): 22 px tall, min 22 px wide, 5 px side padding, square corners.
        /// </summary>
        public static RectTransform Kbd(Transform parent, string name, string key, float x, float y)
        {
            RectTransform root = Node(parent, name);

            Color edge = Girdap.Text;
            edge.a = 0.6f; // CSS `opacity: .6` on currentColor

            UiShape border = Shape(root, "Bg");
            Stretch(border.rectTransform);
            border.Chamfer(0f).Outline(1f, edge).Fill(Color.clear).Antialias(false);

            TextMeshProUGUI text = Text(root, "Label", key, GirdapFont.ChakraBold, 12f, edge,
                TextAlignmentOptions.Center);
            Stretch(text.rectTransform);

            float width = Mathf.Max(22f, Mathf.Ceil(text.GetPreferredValues(key ?? "").x) + 10f);
            Place(root, x, y, width, 22f);
            return root;
        }

        /// <summary>
        /// Themed button. Icon/label/keycap are measured and centered as a group — there is no
        /// Layout Group, so the content block's width is computed here.
        /// </summary>
        public static UiButtonStyle Button(Transform parent, string name, string label,
            UiButtonKind kind, float x, float y, float w, float h, string icon = null,
            string kbd = null, float fontSize = 20f, GirdapFont font = GirdapFont.SairaBold,
            float chamfer = 8f, bool hold = false)
        {
            RectTransform root = Node(parent, name);
            Place(root, x, y, w, h);

            UiShape bg = Shape(root, "Bg");
            Stretch(bg.rectTransform);
            bg.Chamfer(chamfer);
            bg.raycastTarget = true;

            UiShape holdFill = null;
            if (hold)
            {
                // Left-anchored progress fill; width is driven by anchorMax.x at runtime.
                holdFill = Shape(root, "HoldFill");
                RectTransform hr = holdFill.rectTransform;
                hr.anchorMin = new Vector2(0f, 0f);
                hr.anchorMax = new Vector2(1f, 1f);
                hr.pivot = new Vector2(0f, 0.5f);
                hr.offsetMin = Vector2.zero;
                hr.offsetMax = Vector2.zero;
                holdFill.Chamfer(chamfer).Fill(Girdap.HoldBg);
                holdFill.gameObject.SetActive(false);
            }

            // CSS `.btn`/.md/.sm icon box and gap.
            float iconSize = h >= 48f ? 20f : 16f;
            float gap = h >= 48f ? 8f : h >= 36f ? 6f : 5f;

            UiImage iconImage = null;
            if (!string.IsNullOrEmpty(icon))
            {
                iconImage = Icon(root, "Icon", icon, iconSize, Girdap.Text);
            }

            TextMeshProUGUI text = Text(root, "Label", label, font, fontSize, Girdap.Text,
                TextAlignmentOptions.Center, 0.07f);

            RectTransform kbdRect = null;
            if (!string.IsNullOrEmpty(kbd))
            {
                kbdRect = Kbd(root, "Kbd", kbd, 0f, 0f);
            }

            if (iconImage == null && kbdRect == null)
            {
                Stretch(text.rectTransform);
            }
            else
            {
                float labelWidth = Mathf.Ceil(text.GetPreferredValues(text.text).x);
                float kbdWidth = kbdRect != null ? kbdRect.sizeDelta.x : 0f;
                float total = labelWidth
                              + (iconImage != null ? iconSize + gap : 0f)
                              + (kbdRect != null ? kbdWidth + gap : 0f);
                float cursor = (w - total) * 0.5f;

                if (iconImage != null)
                {
                    PlaceMiddleLeft(iconImage.rectTransform, cursor, iconSize, iconSize);
                    cursor += iconSize + gap;
                }

                PlaceMiddleLeft(text.rectTransform, cursor, labelWidth, h);
                cursor += labelWidth;

                if (kbdRect != null)
                {
                    PlaceMiddleLeft(kbdRect, cursor + gap, kbdWidth, 22f);
                }
            }

            var button = root.gameObject.AddComponent<UiButton>();
            var style = root.gameObject.AddComponent<UiButtonStyle>();
            style.Bind(bg, text, iconImage, holdFill, button,
                kbdRect != null ? kbdRect.GetComponentInChildren<UiShape>() : null,
                kbdRect != null ? kbdRect.GetComponentInChildren<TextMeshProUGUI>() : null);
            style.SetKind(kind);
            return style;
        }

        /// <summary>
        /// Status badge. Position is left to the caller (<c>Place</c>/<c>PlaceRight</c>) so a
        /// right-anchored chip can keep its own pivot — this helper never touches it.
        /// </summary>
        public static UiChip Chip(Transform parent, string name, string text, UiChipKind kind,
            string icon = null)
        {
            RectTransform root = Node(parent, name);
            root.sizeDelta = new Vector2(0f, UiChip.Height);

            UiShape bg = Shape(root, "Bg");
            Stretch(bg.rectTransform);
            bg.Chamfer(5f);

            // Icon object always exists (inactive without a sprite): a runtime Set() may add one.
            UiImage iconImage = Image(root, "Icon", LoadIcon(icon), Girdap.Muted);
            iconImage.preserveAspect = true;
            iconImage.rectTransform.sizeDelta = new Vector2(UiChip.IconSize, UiChip.IconSize);

            TextMeshProUGUI label = Text(root, "Label", text, GirdapFont.SairaBold, 13f,
                Girdap.Muted, TextAlignmentOptions.MidlineLeft, 0.08f);

            var chip = root.gameObject.AddComponent<UiChip>();
            chip.Bind(bg, label, iconImage, root);
            chip.Set(text, kind, icon);
            return chip;
        }

        // ---------------------------------------------------------------- prefabs

        /// <summary>Detached root for a prefab under construction.</summary>
        public static GameObject NewRoot(string name, float w, float h)
        {
            var go = new GameObject(name);
            var rt = go.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(w, h);
            return go;
        }

        public static void SavePrefab(GameObject root, string fileName)
        {
            EnsureFolder(PrefabDir);
            PrefabUtility.SaveAsPrefabAsset(root, PrefabDir + fileName + ".prefab");
            Object.DestroyImmediate(root);
        }

        /// <summary>
        /// Opens a prefab for IN-PLACE editing. Used where variants/overrides must survive — saving
        /// a fresh root over such a prefab would reset every variant instead.
        /// </summary>
        public static GameObject LoadPrefabContents(string fileName)
        {
            string path = PrefabDir + fileName + ".prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
            {
                return null;
            }

            return PrefabUtility.LoadPrefabContents(path);
        }

        public static void SaveAndUnload(GameObject contents, string fileName)
        {
            if (contents == null)
            {
                return;
            }

            PrefabUtility.SaveAsPrefabAsset(contents, PrefabDir + fileName + ".prefab");
            PrefabUtility.UnloadPrefabContents(contents);
        }

        /// <summary>Creates every missing folder along an <c>Assets/...</c> path.</summary>
        public static void EnsureFolder(string folder)
        {
            string trimmed = folder.TrimEnd('/');
            if (AssetDatabase.IsValidFolder(trimmed))
            {
                return;
            }

            string[] parts = trimmed.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    // A folder that exists on disk but is not imported yet must be imported, not
                    // re-created: CreateFolder would try to overwrite its .meta and fail.
                    if (System.IO.Directory.Exists(next))
                    {
                        AssetDatabase.ImportAsset(next);
                    }
                    else
                    {
                        AssetDatabase.CreateFolder(current, parts[i]);
                    }
                }

                current = next;
            }
        }
    }
}
