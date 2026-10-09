using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace VortexArena.Core.GripCapture
{
    /// <summary>
    /// Transparent URP Unlit material factory for the tool's ghosts (cached per colour).
    /// <para>⚠️ Built in code, not an asset: a URP Unlit material left opaque draws the ghost solid,
    /// so every blend field is set here explicitly — the shader's defaults are opaque.</para>
    /// </summary>
    public static class GripGhostMaterial
    {
        private const string ShaderName = "Universal Render Pipeline/Unlit";

        private static readonly Dictionary<Color, Material> Cache = new Dictionary<Color, Material>();

        /// <summary>A transparent unlit material of this colour.</summary>
        public static Material Get(Color colour)
        {
            if (Cache.TryGetValue(colour, out Material cached) && cached != null)
            {
                return cached;
            }

            Shader shader = Shader.Find(ShaderName);
            if (shader == null)
            {
                Debug.LogError($"[GripCapture] Shader bulunamadı: {ShaderName} — hayalet modeller " +
                               "görünmeyebilir.");
                return null;
            }

            var material = new Material(shader)
            {
                name = $"GripGhost {colour}",
                renderQueue = 3000,
            };

            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 0f);
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0f);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.SetColor("_BaseColor", colour);
            material.color = colour;

            Cache[colour] = material;
            return material;
        }

        /// <summary>Replaces every renderer material in the subtree with one ghost material.</summary>
        public static void Apply(GameObject root, Color colour)
        {
            Material material = Get(colour);
            if (root == null || material == null)
            {
                return;
            }

            var renderers = root.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                var materials = new Material[renderers[i].sharedMaterials.Length == 0
                    ? 1
                    : renderers[i].sharedMaterials.Length];
                for (int m = 0; m < materials.Length; m++)
                {
                    materials[m] = material;
                }

                renderers[i].sharedMaterials = materials;
            }
        }
    }
}
