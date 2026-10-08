using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using VortexArena.Core.UI;

namespace VortexArena.App.Editor
{
    /// <summary>
    /// Generates the theme's TMP font assets and the <see cref="GirdapAssets"/> container.
    /// </summary>
    public static partial class GirdapUiBuilder
    {
        private const string FallbackFontPath =
            "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset";

        /// <summary>
        /// Characters baked into the atlas up front. Dynamic atlases grow on demand at runtime, but
        /// on Quest that growth is a frame hitch the first time a name with "Ş" appears.
        /// </summary>
        private const string PrebakedCharacters =
            " !\"#$%&'()*+,-./0123456789:;<=>?@" +
            "ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_`" +
            "abcdefghijklmnopqrstuvwxyz{|}~" +
            "İıŞşĞğÜüÖöÇç" +
            "—–·×…°";

        [MenuItem("Tools/VortexArena/UI/Girdap/Fontları ve asset kabını üret")]
        public static void BuildAssets()
        {
            EnsureFolder(FontAssetDir);

            // ⚠️ Before BuildContainer: the icon array is collected off the folder, so a png written
            // afterwards would be missing from the container until the next run.
            EnsureGirdapIcons();

            var fallback = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FallbackFontPath);
            var fonts = new Dictionary<GirdapFont, TMP_FontAsset>();

            foreach (GirdapFont role in System.Enum.GetValues(typeof(GirdapFont)))
            {
                fonts[role] = EnsureFontAsset(role, fallback);
            }

            BuildContainer(fonts);
            EnsureLokantaSprites();
            AssetDatabase.SaveAssets();
        }

        private static TMP_FontAsset EnsureFontAsset(GirdapFont role, TMP_FontAsset fallback)
        {
            string stem = FontFileName(role);
            string path = FontAssetDir + stem + " SDF.asset";

            // ⚠️ Never regenerate an existing asset: a fresh atlas invalidates every glyph index
            // already baked into saved prefabs/scenes and the text re-renders blank until reimport.
            var existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
            if (existing != null)
            {
                return existing;
            }

            var source = AssetDatabase.LoadAssetAtPath<Font>(FontDir + stem + ".ttf");
            if (source == null)
            {
                Debug.LogError($"[Girdap] {FontDir}{stem}.ttf içe aktarılmamış; editöre tıklayıp bekle.");
                return null;
            }

            AtlasMetrics(role, out int pointSize, out int padding, out int atlas);
            TMP_FontAsset fa = TMP_FontAsset.CreateFontAsset(source, pointSize, padding,
                GlyphRenderMode.SDFAA, atlas, atlas, AtlasPopulationMode.Dynamic, true);
            if (fa == null)
            {
                Debug.LogError($"[Girdap] {stem} için TMP font asset üretilemedi.");
                return null;
            }

            AssetDatabase.CreateAsset(fa, path);

            // Atlas + material must be SUB-assets: standalone they would be orphaned on reimport.
            if (fa.atlasTexture != null)
            {
                fa.atlasTexture.name = stem + " Atlas";
                AssetDatabase.AddObjectToAsset(fa.atlasTexture, fa);
            }

            if (fa.material != null)
            {
                fa.material.name = stem + " Material";
                AssetDatabase.AddObjectToAsset(fa.material, fa);
            }

            if (fallback != null)
            {
                // Turkish glyphs live in the theme fonts; the fallback only catches the rest.
                fa.fallbackFontAssetTable = new List<TMP_FontAsset> { fallback };
            }

            fa.TryAddCharacters(PrebakedCharacters);
            EditorUtility.SetDirty(fa);
            return fa;
        }

        // Heavy display faces (Saira ExtraBold, Fredoka) alias badly at 72/8 and need a larger
        // sample + padding; body faces (Barlow, Nunito) fit a 512 atlas.
        private static void AtlasMetrics(GirdapFont role, out int pointSize, out int padding,
            out int atlas)
        {
            switch (role)
            {
                case GirdapFont.SairaExtraBold:
                case GirdapFont.FredokaBold:
                case GirdapFont.FredokaSemiBold:
                    pointSize = 90;
                    padding = 9;
                    atlas = 1024;
                    break;
                case GirdapFont.BarlowMedium:
                case GirdapFont.NunitoExtraBold:
                case GirdapFont.NunitoBold:
                    pointSize = 64;
                    padding = 7;
                    atlas = 512;
                    break;
                default:
                    pointSize = 72;
                    padding = 8;
                    atlas = 1024;
                    break;
            }
        }

        private static void BuildContainer(Dictionary<GirdapFont, TMP_FontAsset> fonts)
        {
            EnsureFolder(PrefabDir);
            string path = PrefabDir + "Girdap.asset";

            var container = AssetDatabase.LoadAssetAtPath<GirdapAssets>(path);
            bool created = container == null;
            if (created)
            {
                container = ScriptableObject.CreateInstance<GirdapAssets>();
            }

            container.chakraBold = fonts[GirdapFont.ChakraBold];
            container.chakraSemiBold = fonts[GirdapFont.ChakraSemiBold];
            container.sairaExtraBold = fonts[GirdapFont.SairaExtraBold];
            container.sairaBold = fonts[GirdapFont.SairaBold];
            container.sairaSemiBold = fonts[GirdapFont.SairaSemiBold];
            container.barlowMedium = fonts[GirdapFont.BarlowMedium];
            container.fredokaBold = fonts[GirdapFont.FredokaBold];
            container.fredokaSemiBold = fonts[GirdapFont.FredokaSemiBold];
            container.nunitoExtraBold = fonts[GirdapFont.NunitoExtraBold];
            container.nunitoBold = fonts[GirdapFont.NunitoBold];

            container.dots = LoadSprite("Dots_16.png");
            container.radial = LoadSprite("Radial_256.png");
            container.icons = CollectIcons();
            container.InvalidateIconLookup();

            if (created)
            {
                AssetDatabase.CreateAsset(container, path);
            }

            EditorUtility.SetDirty(container);
        }

        private static Sprite[] CollectIcons()
        {
            string[] guids = AssetDatabase.FindAssets("Ic_ t:Sprite", new[] { IconDir.TrimEnd('/') });
            var list = new List<Sprite>(guids.Length);

            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                if (sprite != null && sprite.name.StartsWith("Ic_"))
                {
                    list.Add(sprite);
                }
            }

            list.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            return list.ToArray();
        }
    }
}
