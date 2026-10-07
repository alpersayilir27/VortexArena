using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace VortexArena.Core.UI
{
    /// <summary>Makes a world-space UI panel ignore depth and sort above the blackout quad — call
    /// once per root (sorting is shifted, not clamped).</summary>
    /// <remarks>
    /// World-space UI is depth-tested like a mesh, so a wall in front of the panel hides it.
    /// <c>unity_GUIZTestMode</c> cannot be set per material — UI/Default and the TMP shaders do not
    /// declare it, so the value is silently ignored; the clone therefore gets a ZTest Always COPY of
    /// the shader (<c>Shaders/Resources</c>), plus a sorting offset to beat the same-queue blackout
    /// quad nearer the eye.
    /// <para>⚠️ Every UI shader used in the HUD needs a mapping in <see cref="OnTopShader"/> and an
    /// "On Top" copy next to the others — an unmapped shader stays depth-tested.</para>
    /// Content cloned from an already-processed template inherits this; UI added from elsewhere must
    /// call <see cref="Apply"/> itself.
    /// </remarks>
    public static class UiDrawOnTop
    {
        /// <summary>Sorting offset of the in-game HUDs — lifts them above the blackout quad (order 0).</summary>
        public const int HudSortingOffset = 10;

        private const string UiShaderName = "UI/Default";
        private const string TmpMobileShaderName = "TextMeshPro/Mobile/Distance Field";

        // source material → on-top clone, plus the clone set so a second Apply pass is a no-op.
        private static readonly Dictionary<Material, Material> Clones = new Dictionary<Material, Material>();
        private static readonly HashSet<Material> CloneSet = new HashSet<Material>();

        // Warned-about source shader names — one log per shader, not per material.
        private static readonly HashSet<string> Warned = new HashSet<string>();

        private static Shader _uiOnTop;
        private static Shader _tmpMobileOnTop;
        private static bool _shadersLoaded;

        public static void Apply(GameObject root, int sortingOffset)
        {
            if (root == null)
            {
                return;
            }

            Canvas[] canvases = root.GetComponentsInChildren<Canvas>(true);
            for (int i = 0; i < canvases.Length; i++)
            {
                Canvas canvas = canvases[i];

                // A nested canvas without overrideSorting inherits its root's order — leave it alone,
                // otherwise the shift would be applied twice.
                bool isRoot = canvas.transform.parent == null ||
                    canvas.transform.parent.GetComponentInParent<Canvas>(true) == null;
                if (isRoot || canvas.overrideSorting)
                {
                    canvas.sortingOrder += sortingOffset;
                }
            }

            Graphic[] graphics = root.GetComponentsInChildren<Graphic>(true);
            for (int i = 0; i < graphics.Length; i++)
            {
                Graphic graphic = graphics[i];

                // TMP owns its sub-mesh materials and clones them from the parent text's material.
                if (graphic is TMP_SubMeshUI)
                {
                    continue;
                }

                if (graphic is TMP_Text text)
                {
                    text.fontSharedMaterial = OnTop(text.fontSharedMaterial);
                    continue;
                }

                graphic.material = OnTop(graphic.material);
            }
        }

        private static Material OnTop(Material source)
        {
            if (source == null || CloneSet.Contains(source))
            {
                return source;
            }

            if (Clones.TryGetValue(source, out Material cached))
            {
                if (cached != null)
                {
                    return cached;
                }

                // The clone was destroyed (domain reload / scene unload) — drop it and rebuild.
                CloneSet.Remove(cached);
            }

            Shader target = OnTopShader(source.shader);
            if (target == null)
            {
                return source;
            }

            Material clone = new Material(source)
            {
                name = source.name + " (OnTop)",
                hideFlags = HideFlags.HideAndDontSave
            };
            clone.shader = target;

            // The shader swap resets the queue to the copy's own — keep the source's so the sorting
            // order logic above stays the only thing deciding what draws over what.
            clone.renderQueue = source.renderQueue;

            Clones[source] = clone;
            CloneSet.Add(clone);
            return clone;
        }

        /// <summary>The ZTest Always copy of a UI shader; null (with a one-off warning) if unmapped.</summary>
        private static Shader OnTopShader(Shader source)
        {
            if (source == null)
            {
                return null;
            }

            if (!_shadersLoaded)
            {
                _shadersLoaded = true;
                _uiOnTop = Resources.Load<Shader>("UiOnTop");
                _tmpMobileOnTop = Resources.Load<Shader>("TmpSdfMobileOnTop");
            }

            string name = source.name;
            Shader target = name switch
            {
                UiShaderName => _uiOnTop,
                TmpMobileShaderName => _tmpMobileOnTop,
                _ => null
            };

            if (target == null && Warned.Add(name))
            {
                Debug.LogWarning($"[UiDrawOnTop] '{name}' shader'ı için üstte çizim kopyası yok — duvar arkasında kalabilir.");
            }

            return target;
        }
    }
}
