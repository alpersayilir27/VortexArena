using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace VortexArena.Core.UI
{
    /// <summary>
    /// The theme's asset container: fonts + icon sprites, reached through
    /// <see cref="Girdap.Assets"/> (<c>Resources/UI/Girdap.asset</c>).
    /// <para>
    /// ⚠️ <b>Why a container instead of per-screen references:</b> the panels are generated prefabs,
    /// and a renamed/reimported font would otherwise need re-wiring in every one of them.
    /// </para>
    /// Populated by <c>Tools &gt; VortexArena &gt; UI &gt; Girdap</c>.
    /// </summary>
    public class GirdapAssets : ScriptableObject
    {
        [Header("Fonts")]
        public TMP_FontAsset chakraBold;
        public TMP_FontAsset chakraSemiBold;
        public TMP_FontAsset sairaExtraBold;
        public TMP_FontAsset sairaBold;
        public TMP_FontAsset sairaSemiBold;
        public TMP_FontAsset barlowMedium;

        [Header("Fonts (Lokanta)")]
        public TMP_FontAsset fredokaBold;

        public TMP_FontAsset fredokaSemiBold;
        public TMP_FontAsset nunitoExtraBold;
        public TMP_FontAsset nunitoBold;

        [Header("Patterns")]
        public Sprite dots;
        public Sprite radial;

        /// <summary>Icon sprites named <c>Ic_&lt;Name&gt;</c>; looked up by the bare name.</summary>
        [Header("Icons")]
        public Sprite[] icons;

        private Dictionary<string, Sprite> iconLookup;
        private readonly HashSet<string> warned = new HashSet<string>();

        public TMP_FontAsset Font(GirdapFont font)
        {
            switch (font)
            {
                case GirdapFont.ChakraBold: return chakraBold;
                case GirdapFont.ChakraSemiBold: return chakraSemiBold;
                case GirdapFont.SairaExtraBold: return sairaExtraBold;
                case GirdapFont.SairaBold: return sairaBold;
                case GirdapFont.SairaSemiBold: return sairaSemiBold;
                case GirdapFont.BarlowMedium: return barlowMedium;
                case GirdapFont.FredokaBold: return fredokaBold;
                case GirdapFont.FredokaSemiBold: return fredokaSemiBold;
                case GirdapFont.NunitoExtraBold: return nunitoExtraBold;
                case GirdapFont.NunitoBold: return nunitoBold;
                default: return null;
            }
        }

        /// <summary><paramref name="name"/> is the bare icon name ("Skull" → <c>Ic_Skull</c>).</summary>
        public Sprite Icon(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return null;
            }

            if (iconLookup == null)
            {
                BuildLookup();
            }

            if (iconLookup.TryGetValue(name, out Sprite sprite) && sprite != null)
            {
                return sprite;
            }

            // Once per name: a missing icon on a per-frame HUD would flood the console.
            if (warned.Add(name))
            {
                Debug.LogWarning($"[Girdap] Ic_{name} ikonu bulunamadı — UI/Girdap.asset'i yeniden üret.");
            }

            return null;
        }

        private void BuildLookup()
        {
            iconLookup = new Dictionary<string, Sprite>(
                icons != null ? icons.Length : 0, System.StringComparer.OrdinalIgnoreCase);
            if (icons == null)
            {
                return;
            }

            for (int i = 0; i < icons.Length; i++)
            {
                Sprite sprite = icons[i];
                if (sprite == null)
                {
                    continue;
                }

                string key = sprite.name.StartsWith("Ic_") ? sprite.name.Substring(3) : sprite.name;
                iconLookup[key] = sprite;
            }
        }

        /// <summary>Drops the cached icon lookup (editor rebuilds the array in place).</summary>
        public void InvalidateIconLookup()
        {
            iconLookup = null;
            warned.Clear();
        }
    }
}
