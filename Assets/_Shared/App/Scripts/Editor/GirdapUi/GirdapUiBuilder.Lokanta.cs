using TMPro;
using UnityEditor;
using UnityEngine;
using VortexArena.Core.UI;

// Factory methods share element names (Image / Text). The alias is required because
// `Image x = Image(...)` gives CS0119 — simple name lookup finds the member group BEFORE the type.
using UiImage = UnityEngine.UI.Image;

namespace VortexArena.App.Editor
{
    /// <summary>
    /// "Lokanta" skin primitives (<c>Docs/Gelistirici/Arayuz/asci.html</c> + <c>asci.css</c>):
    /// cards, pills, ribbons and the TMP shadow/outline presets the Burger mode screens are built
    /// from. Palette tokens live in <see cref="Lokanta"/>.
    /// <para>
    /// ⚠️ The skin's depth is a SOLID offset plate, not a blur: every card is a container with a
    /// <c>Shadow</c> sibling drawn first. Faking it with <c>UiShape.Glow</c> reads as a halo.
    /// </para>
    /// </summary>
    public static partial class GirdapUiBuilder
    {
        public const string LkSpriteDir = "Assets/Modes/Burger/UI/Sprites/";
        public const string LkUiDir = "Assets/Modes/Burger/UI/";

        // Screen builders land in later partials; an unimplemented partial method call is dropped by
        // the compiler, so this file compiles on its own.
        static partial void BuildLokanta();

        [MenuItem("Tools/VortexArena/UI/Girdap/Lokanta sprite'larını üret")]
        public static void BuildLokantaSprites()
        {
            EnsureLokantaSprites();
            AssetDatabase.SaveAssets();
        }

        // ------------------------------------------------------------- surfaces

        /// <summary>
        /// Cream card with an Ink outline and a solid drop plate.
        /// <para>
        /// ⚠️ Returns the <c>Plate</c>, and the node the CALLER must place is its parent
        /// (<c>(RectTransform)plate.transform.parent</c>, named <paramref name="name"/>): the shadow
        /// has to be an earlier SIBLING to render behind, and a child would render on top.
        /// </para>
        /// </summary>
        public static UiShape LkCard(Transform parent, string name, float radius, Color fill,
            float outline = Lokanta.Outline, float shadow = Lokanta.Shadow)
        {
            RectTransform root = Node(parent, name);

            if (shadow > 0f)
            {
                UiShape drop = Shape(root, "Shadow");
                Stretch(drop.rectTransform, 0f, shadow, 0f, -shadow);
                drop.Radius(radius).Fill(Lokanta.Ink);
            }

            UiShape plate = Shape(root, "Plate");
            Stretch(plate.rectTransform);
            plate.Radius(radius).Fill(fill);
            if (outline > 0f)
            {
                plate.Outline(outline, Lokanta.Ink);
            }

            return plate;
        }

        /// <summary><see cref="LkCard"/> rounded to a capsule. <paramref name="h"/> is the pill's own
        /// height — the helper cannot read it off an unplaced rect.</summary>
        public static UiShape LkPill(Transform parent, string name, float h, Color fill,
            float outline = Lokanta.Outline, float shadow = Lokanta.Shadow)
        {
            return LkCard(parent, name, h * 0.5f, fill, outline, shadow);
        }

        /// <summary>
        /// Label ribbon: gradient capsule, Ink outline, an inset accent line and (red only) two
        /// rows of tiled dots. Sized but NOT positioned — the caller places the returned container.
        /// </summary>
        public static RectTransform LkRibbon(Transform parent, string name, string text, float w,
            float h, GirdapFont font, float size, bool must = false)
        {
            RectTransform root = Node(parent, name);
            root.sizeDelta = new Vector2(w, h);

            UiShape bg = Shape(root, "Bg");
            Stretch(bg.rectTransform);
            bg.Radius(h * 0.5f)
                .Outline(3f, Lokanta.Ink)
                .Fill(must ? Lokanta.MustHi : Lokanta.RedHi,
                    must ? Lokanta.Must2 : Lokanta.Red2, UiGradientMode.Vertical);

            UiShape line = Shape(root, "Line");
            Stretch(line.rectTransform, 3f, 3f, 3f, 3f);
            line.Radius(Mathf.Max(h * 0.5f - 3f, 0f))
                .Outline(3f, must ? Lokanta.Light : Lokanta.Must)
                .Fill(Color.clear);

            if (!must)
            {
                Sprite dot = LkSprite("Lk_Dot");
                UiImage top = Image(root, "DotsTop", dot, Color.white, UiImage.Type.Tiled);
                StretchTop(top.rectTransform, 6f, 6f, 6f, 4f);

                UiImage bottom = Image(root, "DotsBottom", dot, Color.white, UiImage.Type.Tiled);
                StretchBottom(bottom.rectTransform, 6f, 6f, 6f, 4f);
            }

            TextMeshProUGUI label = LkText(root, "Label", text, font, size,
                must ? Lokanta.Ink : Lokanta.Light, TextAlignmentOptions.Center);
            Stretch(label.rectTransform);
            if (!must)
            {
                TextShadow(label, Lokanta.Ink);
            }

            return root;
        }

        // ---------------------------------------------------------------- content

        /// <summary>Customer mood face sprite (<c>Lk_FaceHappy/Meh/Sad.png</c>).</summary>
        public static UiImage LkFace(Transform parent, string name, LkMood mood, float size)
        {
            UiImage image = Image(parent, name, LkSprite("Lk_Face" + mood), Color.white);
            image.preserveAspect = true;
            image.rectTransform.sizeDelta = new Vector2(size, size);
            return image;
        }

        /// <summary><see cref="Text"/> with the skin's defaults (no rich text, no uppercase style —
        /// the mockup's caps are already in the string).</summary>
        public static TextMeshProUGUI LkText(Transform parent, string name, string text,
            GirdapFont font, float size, Color color, TextAlignmentOptions align,
            float spacingEm = 0f)
        {
            return Text(parent, name, text, font, size, color, align, spacingEm);
        }

        // ------------------------------------------------------- tmp text presets

        /// <summary>
        /// Hard text shadow: a TMP material preset with Underlay on, one asset per font + colour.
        /// ⚠️ Must be an ASSET, not a runtime keyword: <c>UNDERLAY_ON</c> is a shader_feature and the
        /// build strips the variant unless a material in the build uses it.
        /// ⚠️ <paramref name="dy"/> is in SDF/em units and is NOT part of the asset name, so the last
        /// caller for a given font + colour wins.
        /// </summary>
        public static void TextShadow(TextMeshProUGUI tmp, Color color, float dy = 2f)
        {
            Material mat = TextPreset(tmp, "Shadow", color);
            if (mat == null)
            {
                return;
            }

            mat.EnableKeyword(ShaderUtilities.Keyword_Underlay);
            mat.SetColor(ShaderUtilities.ID_UnderlayColor, color);
            mat.SetFloat(ShaderUtilities.ID_UnderlayOffsetX, 0f);
            mat.SetFloat(ShaderUtilities.ID_UnderlayOffsetY, -dy);
            mat.SetFloat(ShaderUtilities.ID_UnderlaySoftness, 0f);
            mat.SetFloat(ShaderUtilities.ID_UnderlayDilate, 0f);
            EditorUtility.SetDirty(mat);
            tmp.fontSharedMaterial = mat;
        }

        /// <summary>
        /// Ink-outlined text (the skin's big numbers), optionally with a hard shadow on the same
        /// preset. <paramref name="width01"/> is SDF units (0..1), not px.
        /// ⚠️ Same naming caveat as <see cref="TextShadow"/>: the asset is keyed by font + outline
        /// colour only.
        /// </summary>
        public static void TextOutline(TextMeshProUGUI tmp, Color color, float width01,
            Color shadow = default, float dy = 0f)
        {
            Material mat = TextPreset(tmp, "Outline", color);
            if (mat == null)
            {
                return;
            }

            mat.EnableKeyword(ShaderUtilities.Keyword_Outline);
            mat.SetFloat(ShaderUtilities.ID_OutlineWidth, Mathf.Clamp01(width01));
            mat.SetColor(ShaderUtilities.ID_OutlineColor, color);
            mat.SetFloat(ShaderUtilities.ID_FaceDilate, 0f);

            if (shadow.a > 0f)
            {
                mat.EnableKeyword(ShaderUtilities.Keyword_Underlay);
                mat.SetColor(ShaderUtilities.ID_UnderlayColor, shadow);
                mat.SetFloat(ShaderUtilities.ID_UnderlayOffsetX, 0f);
                mat.SetFloat(ShaderUtilities.ID_UnderlayOffsetY, -dy);
                mat.SetFloat(ShaderUtilities.ID_UnderlaySoftness, 0f);
                mat.SetFloat(ShaderUtilities.ID_UnderlayDilate, 0f);
            }

            EditorUtility.SetDirty(mat);
            tmp.fontSharedMaterial = mat;
        }

        // Loads or creates the "<font> <kind> <RRGGBBAA>.mat" preset next to the glow presets.
        private static Material TextPreset(TextMeshProUGUI tmp, string kind, Color color)
        {
            if (tmp == null || tmp.font == null || tmp.font.material == null)
            {
                return null;
            }

            Material baseMaterial = tmp.font.material;
            string path = FontAssetDir + tmp.font.name + " " + kind + " "
                          + ColorUtility.ToHtmlStringRGBA(color) + ".mat";

            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(baseMaterial);
                AssetDatabase.CreateAsset(mat, path);
            }

            mat.shader = baseMaterial.shader;
            mat.SetTexture(ShaderUtilities.ID_MainTex,
                baseMaterial.GetTexture(ShaderUtilities.ID_MainTex));
            return mat;
        }

        // --------------------------------------------------------------- sprites

        /// <summary>Skin sprite by bare name ("Lk_Star" → <c>Lk_Star.png</c> under
        /// <see cref="LkSpriteDir"/>).</summary>
        public static Sprite LkSprite(string fileName)
        {
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(LkSpriteDir + fileName + ".png");
            if (sprite == null)
            {
                Debug.LogWarning($"[Lokanta] {fileName}.png bulunamadı: {LkSpriteDir}");
            }

            return sprite;
        }
    }

    /// <summary>Customer mood buckets; the thresholds live in <see cref="Lokanta"/>.</summary>
    public enum LkMood
    {
        Happy,
        Meh,
        Sad
    }
}
