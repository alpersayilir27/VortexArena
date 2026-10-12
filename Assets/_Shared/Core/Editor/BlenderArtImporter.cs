using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace VortexArena.Core.Editor
{
    /// <summary>Sets up a Blender-exported art folder (<c>&lt;Art&gt;/*_Materials.json</c> + Textures + Models).</summary>
    /// <remarks>
    /// Texture import settings (ASTC per map kind), one URP Lit material per JSON entry in <c>&lt;Art&gt;/Materials</c>
    /// (albedo, normal, mask in both Metallic and Occlusion slots, alpha clip, two-sided, emission) and the FBX
    /// importers (scale, lightmap UVs, slots remapped to those materials by name). Re-running rewrites the
    /// materials in place, so references survive. Pipeline: <c>blender/&lt;Proje&gt;/README.md</c>.
    /// </remarks>
    public static class BlenderArtImporter
    {
        private const string Tag = "[BlenderArt] ";

        [MenuItem("Tools/VortexArena/Art/Blender Sanatını Kur (seçili Art klasörü)", false, 40)]
        private static void SetupSelected()
        {
            string folder = AssetDatabase.GetAssetPath(Selection.activeObject);
            if (string.IsNullOrEmpty(folder) || !AssetDatabase.IsValidFolder(folder))
            {
                Debug.LogError(Tag + "Project penceresinde Blender çıktısının Art klasörünü seçin.");
                return;
            }

            Setup(folder);
        }

        /// <summary>Runs the whole setup on one art folder; returns a one-line summary.</summary>
        public static string Setup(string artFolder)
        {
            AssetDatabase.Refresh();                        // the exporter writes straight into the folder
            string jsonPath = Directory.GetFiles(artFolder, "*_Materials.json").FirstOrDefault();
            if (jsonPath == null)
            {
                Debug.LogError(Tag + $"{artFolder} içinde *_Materials.json yok.");
                return "no json";
            }

            JObject root = JObject.Parse(File.ReadAllText(jsonPath));
            var entries = (JObject)root["materials"];
            string tex = artFolder + "/Textures";
            string matDir = artFolder + "/Materials";
            if (!AssetDatabase.IsValidFolder(matDir))
            {
                AssetDatabase.CreateFolder(artFolder, "Materials");
            }

            int textures = ConfigureTextures(tex, entries);
            var materials = new Dictionary<string, Material>();
            foreach (KeyValuePair<string, JToken> e in entries)
            {
                materials[e.Key] = BuildMaterial(matDir, tex, e.Key, (JObject)e.Value);
            }

            AssetDatabase.SaveAssets();
            int models = ConfigureModels(artFolder + "/Models", materials, out List<string> missing);
            string summary = $"{textures} doku, {materials.Count} malzeme, {models} model kuruldu";
            if (missing.Count > 0)
            {
                Debug.LogWarning(Tag + "JSON'da karşılığı olmayan slotlar: " + string.Join(", ", missing.Distinct()));
            }

            Debug.Log(Tag + summary + ".");
            return summary;
        }

        private static int ConfigureTextures(string texDir, JObject entries)
        {
            var alpha = new HashSet<string>(entries.Properties()
                .Where(p => (bool?)p.Value["alphaClip"] == true)
                .Select(p => (string)p.Value["baseMap"]));
            int count = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { texDir }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!(AssetImporter.GetAtPath(path) is TextureImporter ti))
                {
                    continue;
                }

                string file = Path.GetFileName(path);
                string kind = Path.GetFileNameWithoutExtension(path).Split('_').Last();   // A | N | M
                ti.textureType = kind == "N" ? TextureImporterType.NormalMap : TextureImporterType.Default;
                ti.sRGBTexture = kind == "A";
                ti.alphaSource = kind == "A" && !alpha.Contains(file) ? TextureImporterAlphaSource.None
                                                                      : TextureImporterAlphaSource.FromInput;
                ti.alphaIsTransparency = kind == "A" && alpha.Contains(file);
                ti.mipmapEnabled = true;
                ti.wrapMode = TextureWrapMode.Repeat;
                ti.maxTextureSize = 1024;
                // Quest: ASTC 6x6, normals 5x5 (they band at 6x6)
                ti.SetPlatformTextureSettings(new TextureImporterPlatformSettings
                {
                    name = "Android",
                    overridden = true,
                    maxTextureSize = 1024,
                    format = kind == "N" ? TextureImporterFormat.ASTC_5x5 : TextureImporterFormat.ASTC_6x6,
                });
                ti.SaveAndReimport();
                count++;
            }

            return count;
        }

        private static Material BuildMaterial(string matDir, string texDir, string name, JObject e)
        {
            string path = $"{matDir}/{name}.mat";
            Shader lit = Shader.Find("Universal Render Pipeline/Lit");
            Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(lit) { name = name };
                AssetDatabase.CreateAsset(m, path);
            }
            else
            {
                m.shader = lit;
            }

            Texture2D Load(string key)
            {
                string file = (string)e[key];
                return string.IsNullOrEmpty(file) ? null : AssetDatabase.LoadAssetAtPath<Texture2D>($"{texDir}/{file}");
            }

            Texture2D baseMap = Load("baseMap"), normal = Load("normalMap"), mask = Load("maskMap");
            m.SetTexture("_BaseMap", baseMap);
            m.SetColor("_BaseColor", Color.white);
            m.SetTexture("_BumpMap", normal);
            SetKeyword(m, "_NORMALMAP", normal != null);
            // mask: R metallic, A smoothness (URP metallic map), G AO (URP reads occlusion from G)
            m.SetTexture("_MetallicGlossMap", mask);
            m.SetTexture("_OcclusionMap", mask);
            m.SetFloat("_Smoothness", 1f);                                     // scale on the map's alpha
            m.SetFloat("_SmoothnessTextureChannel", 0f);
            m.SetFloat("_OcclusionStrength", 1f);
            SetKeyword(m, "_METALLICSPECGLOSSMAP", mask != null);
            SetKeyword(m, "_OCCLUSIONMAP", mask != null);

            bool clip = (bool?)e["alphaClip"] == true;
            m.SetFloat("_AlphaClip", clip ? 1f : 0f);
            m.SetFloat("_Cutoff", 0.5f);
            SetKeyword(m, "_ALPHATEST_ON", clip);
            m.renderQueue = clip ? (int)RenderQueue.AlphaTest : (int)RenderQueue.Geometry;

            bool twoSided = (bool?)e["twoSided"] == true;
            m.SetFloat("_Cull", twoSided ? (float)CullMode.Off : (float)CullMode.Back);
            m.doubleSidedGI = twoSided;

            var em = e["emission"] as JObject;
            if (em != null)
            {
                float k = (float)em["intensity"];
                var c = (JArray)em["color"];
                m.SetColor("_EmissionColor", new Color((float)c[0], (float)c[1], (float)c[2]) * k);
                m.SetTexture("_EmissionMap", (bool)em["fromBase"] ? baseMap : null);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;   // lights the bake
            }
            else
            {
                m.SetColor("_EmissionColor", Color.black);
                m.SetTexture("_EmissionMap", null);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            }

            SetKeyword(m, "_EMISSION", em != null);
            EditorUtility.SetDirty(m);
            return m;
        }

        private static int ConfigureModels(string modelDir, Dictionary<string, Material> materials, out List<string> missing)
        {
            missing = new List<string>();
            int count = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { modelDir }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!(AssetImporter.GetAtPath(path) is ModelImporter mi))
                {
                    continue;
                }

                mi.globalScale = 1f;
                mi.useFileScale = true;
                mi.generateSecondaryUV = true;              // lightmap UVs (the arena is baked)
                mi.isReadable = false;
                mi.importAnimation = false;
                mi.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
                mi.materialLocation = ModelImporterMaterialLocation.InPrefab;
                IEnumerable<string> slots = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Renderer>()
                    .SelectMany(r => r.sharedMaterials).Where(x => x != null).Select(x => x.name).Distinct();
                foreach (string slot in slots)
                {
                    if (materials.TryGetValue(slot, out Material mat))
                    {
                        mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), slot), mat);
                    }
                    else
                    {
                        missing.Add(slot);
                    }
                }

                mi.SaveAndReimport();
                count++;
            }

            return count;
        }

        private static void SetKeyword(Material m, string keyword, bool on)
        {
            if (on)
            {
                m.EnableKeyword(keyword);
            }
            else
            {
                m.DisableKeyword(keyword);
            }
        }
    }
}
