using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using VortexArena.Core.Arena;
using Object = UnityEngine.Object;

namespace VortexArena.Core.Editor
{
    /// <summary>Produces/updates the floor-portal kit: <c>M_FloorPortal.mat</c>,
    /// <c>M_FloorPortalFill.mat</c> and <c>VA_FloorPortal.prefab</c> (ring pair + fill discs +
    /// audio sources, all wired into <see cref="FloorPortal"/>).
    /// <para>⚠️ The prefab is rewritten IN PLACE at the same path (<see cref="PrefabPath"/>), so
    /// scene instances placed by <see cref="TemplateBasicsLoader"/> keep their links; a
    /// delete + recreate would break every arena that already uses a portal.</para>
    /// <para>⚠️ No collider anywhere on the portal: the discs are pure art. A collider would catch
    /// fire rays and grabs, and the stand-in test is done in code by distance.</para>
    /// <para>⚠️ No dialogs — output goes to the console (same rule as
    /// <c>WeaponKitBuilder</c>: a modal would lock the pipeline).</para></summary>
    public static class FloorPortalKitBuilder
    {
        // ----------------------------------------------------------------- constants

        private const string Log = "[FloorPortalKit] ";

        private const string PrefabDir = "Assets/_Shared/App/Prefabs";

        /// <summary>Path of the portal prefab — <c>public</c> because
        /// <see cref="TemplateBasicsLoader"/> instantiates it; repeating the path there would be a
        /// second source of truth.</summary>
        public const string PrefabPath = PrefabDir + "/VA_FloorPortal.prefab";

        private const string MaterialDir = "Assets/_Shared/Materials";
        private const string RingMaterialPath = MaterialDir + "/M_FloorPortal.mat";
        private const string FillMaterialPath = MaterialDir + "/M_FloorPortalFill.mat";

        /// <summary>Default upper-floor height (m) — the real value is set per arena on the scene
        /// instance, this is only the prefab's starting point.</summary>
        private const float DefaultUpperHeight = 3f;

        // Disc geometry. Unity's cylinder primitive is 1 m across and 2 m tall, so 0.5 scale = a
        // 0.5 m wide disc and 0.01 = a 2 cm thick plate.
        private const float DiscRadiusScale = 0.5f;
        private const float RingThicknessScale = 0.01f;
        private const float FillThicknessScale = 0.012f;

        /// <summary>Fill lift above the ring (m) — without it the two coplanar discs z-fight.</summary>
        private const float FillLift = 0.006f;

        private static readonly Color RingColor = new Color(0.35f, 0.85f, 1f, 0.45f);
        private static readonly Color FillColor = new Color(0.9f, 1f, 1f, 0.7f);

        /// <summary>Shader search chain (first hit wins). The material is an ASSET, so the shader is
        /// guaranteed into the build — a runtime <c>Shader.Find</c> would be stripped.</summary>
        private static readonly string[] ShaderCandidates =
        {
            "Universal Render Pipeline/Unlit",
            "Universal Render Pipeline/Lit",
            "Sprites/Default",
        };

        private const string LowerDiscName = "Alt";
        private const string UpperDiscName = "Üst";
        private const string FillName = "Dolum";

        // ----------------------------------------------------------------- entry point

        [MenuItem("Tools/VortexArena/Arena/Kat Portalı Kitini Üret", false, 4)]
        public static void BuildKit()
        {
            Material ring = EnsureMaterial(RingMaterialPath, "M_FloorPortal", RingColor);
            Material fill = EnsureMaterial(FillMaterialPath, "M_FloorPortalFill", FillColor);

            if (ring == null || fill == null)
            {
                Debug.LogError(Log + "Kat portalı malzemesi için shader bulunamadı " +
                               "(URP Unlit/Lit, Sprites/Default) — prefab üretilmedi.");
                return;
            }

            BuildPrefab(ring, fill);

            AssetDatabase.SaveAssets();
        }

        // ----------------------------------------------------------------- materials

        /// <summary>Creates the transparent material or rewrites an existing one IN PLACE (the asset
        /// keeps its GUID, so prefab/scene references survive).
        /// <para>On URP shaders transparency needs <c>_Surface</c>/<c>_Blend</c>/<c>_SrcBlend</c>/
        /// <c>_DstBlend</c>/<c>_ZWrite</c> + the <c>_SURFACE_TYPE_TRANSPARENT</c> keyword + the
        /// Transparent queue (exactly what "Surface Type = Transparent" writes in the Inspector).
        /// Writing a non-existent property is a silent no-op, so the fallback shaders pass through
        /// the same code.</para></summary>
        private static Material EnsureMaterial(string path, string name, Color color)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool created = material == null;

            if (created)
            {
                Shader shader = null;
                for (int i = 0; i < ShaderCandidates.Length && shader == null; i++)
                {
                    shader = Shader.Find(ShaderCandidates[i]);
                }

                if (shader == null)
                {
                    return null;
                }

                EnsureFolder(MaterialDir);
                material = new Material(shader) { name = name };
            }

            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 0f);
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            material.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0f);
            material.SetFloat("_AlphaClip", 0f);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.SetOverrideTag("RenderType", "Transparent");
            material.renderQueue = (int)RenderQueue.Transparent;

            // URP reads _BaseColor, the fallbacks _Color; both are written (missing one is ignored).
            material.SetColor("_BaseColor", color);
            material.SetColor("_Color", color);

            if (created)
            {
                AssetDatabase.CreateAsset(material, path);
                Debug.Log(Log + name + ".mat üretildi (" + path + ").");
            }
            else
            {
                EditorUtility.SetDirty(material);
                Debug.Log(Log + name + ".mat güncellendi (" + path + ").");
            }

            return material;
        }

        // ----------------------------------------------------------------- prefab

        /// <summary>Builds the portal hierarchy in memory and saves it over
        /// <see cref="PrefabPath"/>.</summary>
        private static void BuildPrefab(Material ring, Material fill)
        {
            bool existed = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null;
            var live = new List<GameObject>();

            var root = new GameObject("VA_FloorPortal");
            live.Add(root);
            root.layer = 0; // Default: the portal is art, it is not on Obstacle.

            Transform lower = CreateDisc(root.transform, LowerDiscName, Vector3.zero, ring, fill, live);
            Transform upper = CreateDisc(root.transform, UpperDiscName,
                new Vector3(0f, DefaultUpperHeight, 0f), ring, fill, live);

            // Spatial, clip-less sources: the clips are bound per arena on the instance.
            AudioSource charge = CreateAudio(root, true);
            AudioSource teleport = CreateAudio(root, false);
            AudioSource busy = CreateAudio(root, false);

            var portal = root.AddComponent<FloorPortal>();
            var serialized = new SerializedObject(portal);
            SetFloat(serialized, "upperHeight", DefaultUpperHeight);
            SetReference(serialized, "lowerDisc", lower);
            SetReference(serialized, "upperDisc", upper);
            SetReference(serialized, "lowerFill", lower.Find(FillName));
            SetReference(serialized, "upperFill", upper.Find(FillName));
            SetReference(serialized, "chargeSound", charge);
            SetReference(serialized, "teleportSound", teleport);
            SetReference(serialized, "busySound", busy);
            serialized.ApplyModifiedPropertiesWithoutUndo();

            EnsureFolder(PrefabDir);
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath, out bool saved);

            for (int i = 0; i < live.Count; i++)
            {
                if (live[i] != null)
                {
                    Object.DestroyImmediate(live[i]);
                }
            }

            if (!saved)
            {
                Debug.LogError(Log + "VA_FloorPortal: SaveAsPrefabAsset başarısız: " + PrefabPath);
                return;
            }

            Debug.Log(Log + (existed ? "VA_FloorPortal.prefab güncellendi (" : "VA_FloorPortal.prefab üretildi (") +
                      PrefabPath + "): Alt + Üst diski, Dolum diskleri ve üç AudioSource bağlandı.");
            Debug.Log(Log + "ELDE: portalı sahneye 'Tools > VortexArena > Arena > Template Temellerini Yükle' " +
                      "ile koy, yerini ve upperHeight'ını gerçek yerleşime göre ayarla, ses kliplerini bağla.");
        }

        /// <summary>One disc (ring + empty fill child), cylinder primitive without collider.</summary>
        /// <remarks>The fill starts at scale 0 on x/z — the charge animation grows it, so a
        /// non-zero start would read as "already charging".</remarks>
        private static Transform CreateDisc(
            Transform parent,
            string name,
            Vector3 localPosition,
            Material ring,
            Material fill,
            List<GameObject> live)
        {
            GameObject disc = CreateFlatCylinder(name, ring,
                new Vector3(DiscRadiusScale, RingThicknessScale, DiscRadiusScale), live);
            disc.transform.SetParent(parent, false);
            disc.transform.localPosition = localPosition;

            GameObject fillDisc = CreateFlatCylinder(FillName, fill,
                new Vector3(0f, FillThicknessScale, 0f), live);
            fillDisc.transform.SetParent(disc.transform, false);
            // Lift is in the PARENT's scale: the ring's y scale is 0.01, so the local offset is
            // divided back out to land FillLift metres above the ring.
            fillDisc.transform.localPosition = new Vector3(0f, FillLift / RingThicknessScale, 0f);
            fillDisc.transform.localScale = new Vector3(
                0f, FillThicknessScale / RingThicknessScale, 0f);

            return disc.transform;
        }

        /// <summary>Collider-free cylinder primitive with shadows disabled.</summary>
        private static GameObject CreateFlatCylinder(
            string name, Material material, Vector3 scale, List<GameObject> live)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = name;
            go.layer = 0;
            live.Add(go);

            Collider collider = go.GetComponent<Collider>();
            if (collider != null)
            {
                Object.DestroyImmediate(collider);
            }

            go.transform.localScale = scale;

            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;

            return go;
        }

        private static AudioSource CreateAudio(GameObject root, bool loop)
        {
            var source = root.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = loop;
            source.spatialBlend = 1f;
            source.clip = null;
            return source;
        }

        // ----------------------------------------------------------------- helpers

        /// <summary>Writes a serialized float; warns when the field is gone (contract drift).</summary>
        private static void SetFloat(SerializedObject serialized, string field, float value)
        {
            SerializedProperty property = serialized.FindProperty(field);
            if (property == null)
            {
                Debug.LogWarning(Log + "FloorPortal'da '" + field + "' alanı yok — yazılamadı.");
                return;
            }

            property.floatValue = value;
        }

        /// <inheritdoc cref="SetFloat"/>
        private static void SetReference(SerializedObject serialized, string field, Object value)
        {
            SerializedProperty property = serialized.FindProperty(field);
            if (property == null)
            {
                Debug.LogWarning(Log + "FloorPortal'da '" + field + "' alanı yok — bağlanamadı.");
                return;
            }

            property.objectReferenceValue = value;
        }

        /// <summary>Creates an "Assets/..." folder chain, filling in missing links.</summary>
        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            string[] parts = path.Split('/');
            string current = parts[0]; // "Assets"
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                }

                current = next;
            }
        }
    }
}
