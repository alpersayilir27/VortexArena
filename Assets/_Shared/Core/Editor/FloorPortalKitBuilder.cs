using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using VortexArena.Core.Arena;
using Object = UnityEngine.Object;

namespace VortexArena.Core.Editor
{
    /// <summary>Produces/updates the floor-portal kit: materials, band/arrow meshes, band gradient
    /// and <c>VA_FloorPortal.prefab</c> (per disc: plate + fill + glowing band + up/down arrow,
    /// plus audio sources, all wired into <see cref="FloorPortal"/>).
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
        private const string BandMaterialPath = MaterialDir + "/M_FloorPortalBand.mat";
        private const string BandTexturePath = MaterialDir + "/T_FloorPortalBand.asset";
        private const string BandMeshPath = PrefabDir + "/VA_FloorPortal_Band.asset";
        private const string ArrowMeshPath = PrefabDir + "/VA_FloorPortal_Arrow.asset";

        /// <summary>Default upper-floor height (m) — the real value is set per arena on the scene
        /// instance, this is only the prefab's starting point.</summary>
        private const float DefaultUpperHeight = 3f;

        // Plate geometry. Unity's cylinder primitive is 1 m across and 2 m tall, so 0.5 scale = a
        // 0.5 m wide disc and 0.01 = a 2 cm thick plate.
        private const float DiscRadiusScale = 0.5f;
        private const float PlateThicknessScale = 0.01f;
        private const float PlateHalfThickness = PlateThicknessScale; // 2 m primitive × scale / 2
        private const float FillThicknessScale = 0.012f;

        /// <summary>Plate centre above the floor (m): the plate's underside sits 1.5 cm up, so floor
        /// finishes and cage plates do not swallow it.</summary>
        private const float PlateLift = 0.025f;

        /// <summary>Fill lift above the plate centre (m) — without it the two coplanar discs z-fight.</summary>
        private const float FillLift = 0.006f;

        // Band: open tube just outside the disc edge, fading out upwards.
        private const float BandRadius = 0.27f;
        private const float BandHeight = 0.35f;
        private const int BandSegments = 48;
        private const int BandTextureHeight = 32;

        // Arrow (pointing +Y, pivot at its centre): two crossed flat outlines, visible from any side.
        private const float ArrowShaftHalfWidth = 0.05f;
        private const float ArrowShaftHeight = 0.2f;
        private const float ArrowHeadHalfWidth = 0.14f;
        private const float ArrowTotalHeight = 0.36f;

        private static readonly Color RingColor = new Color(0.35f, 0.85f, 1f, 0.6f);
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
        private const string PlateName = "Plaka";
        private const string FillName = "Dolum";
        private const string BandName = "Halka";
        private const string ArrowName = "Ok";

        /// <summary>Generated assets the disc builder needs.</summary>
        private struct Kit
        {
            public Material Ring;
            public Material Fill;
            public Material Band;
            public Mesh BandMesh;
            public Mesh ArrowMesh;
        }

        // ----------------------------------------------------------------- entry point

        [MenuItem("Tools/VortexArena/Arena/Kat Portalı Kitini Üret", false, 4)]
        public static void BuildKit()
        {
            var kit = new Kit
            {
                Ring = EnsureMaterial(RingMaterialPath, "M_FloorPortal", RingColor),
                Fill = EnsureMaterial(FillMaterialPath, "M_FloorPortalFill", FillColor),
                Band = EnsureMaterial(BandMaterialPath, "M_FloorPortalBand", RingColor),
            };

            if (kit.Ring == null || kit.Fill == null || kit.Band == null)
            {
                Debug.LogError(Log + "Kat portalı malzemesi için shader bulunamadı " +
                               "(URP Unlit/Lit, Sprites/Default) — prefab üretilmedi.");
                return;
            }

            // The fade lives in the texture: URP Unlit ignores vertex colours.
            Texture2D gradient = EnsureBandTexture();
            kit.Band.SetTexture("_BaseMap", gradient);
            kit.Band.SetTexture("_MainTex", gradient);
            EditorUtility.SetDirty(kit.Band);

            kit.BandMesh = EnsureMesh(BandMeshPath, "VA_FloorPortal_Band", BuildBandMesh);
            kit.ArrowMesh = EnsureMesh(ArrowMeshPath, "VA_FloorPortal_Arrow", BuildArrowMesh);

            BuildPrefab(kit);

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
            // Two-sided: the band is seen from inside and the flat arrow from behind.
            material.SetFloat("_Cull", (float)CullMode.Off);
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
        private static void BuildPrefab(Kit kit)
        {
            bool existed = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null;
            var live = new List<GameObject>();

            var root = new GameObject("VA_FloorPortal");
            live.Add(root);
            root.layer = 0; // Default: the portal is art, it is not on Obstacle.

            Transform lower = CreateDisc(root.transform, LowerDiscName, Vector3.zero, false, kit, live);
            Transform upper = CreateDisc(root.transform, UpperDiscName,
                new Vector3(0f, DefaultUpperHeight, 0f), true, kit, live);

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
            SetReference(serialized, "lowerArrow", lower.Find(ArrowName));
            SetReference(serialized, "upperArrow", upper.Find(ArrowName));
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
                      PrefabPath + "): Alt + Üst diski (plaka, dolum, halka, ok) ve üç AudioSource bağlandı.");
            Debug.Log(Log + "ELDE: portalı sahneye 'Tools > VortexArena > Arena > Template Temellerini Yükle' " +
                      "ile koy, yerini ve upperHeight'ını gerçek yerleşime göre ayarla, ses kliplerini bağla.");
        }

        /// <summary>One disc: an UNSCALED anchor at floor level (the hop logic reads its XZ) holding
        /// plate, fill, band and arrow, all collider-free.</summary>
        /// <remarks>The fill starts at scale 0 on x/z — the charge animation grows it, so a
        /// non-zero start would read as "already charging".</remarks>
        private static Transform CreateDisc(
            Transform parent,
            string name,
            Vector3 localPosition,
            bool arrowDown,
            Kit kit,
            List<GameObject> live)
        {
            var disc = new GameObject(name) { layer = 0 };
            live.Add(disc);
            disc.transform.SetParent(parent, false);
            disc.transform.localPosition = localPosition;

            GameObject plate = CreateFlatCylinder(PlateName, kit.Ring,
                new Vector3(DiscRadiusScale, PlateThicknessScale, DiscRadiusScale), live);
            Attach(plate, disc.transform, new Vector3(0f, PlateLift, 0f));

            GameObject fill = CreateFlatCylinder(FillName, kit.Fill,
                new Vector3(0f, FillThicknessScale, 0f), live);
            Attach(fill, disc.transform, new Vector3(0f, PlateLift + FillLift, 0f));

            GameObject band = CreateMeshObject(BandName, kit.BandMesh, kit.Band, live);
            Attach(band, disc.transform, new Vector3(0f, PlateLift - PlateHalfThickness, 0f));

            GameObject arrow = CreateMeshObject(ArrowName, kit.ArrowMesh, kit.Ring, live);
            Attach(arrow, disc.transform, new Vector3(0f, FloorPortal.ArrowHeightMeters, 0f));
            if (arrowDown)
            {
                arrow.transform.localRotation = Quaternion.Euler(0f, 0f, 180f);
            }

            return disc.transform;
        }

        private static void Attach(GameObject child, Transform parent, Vector3 localPosition)
        {
            child.transform.SetParent(parent, false);
            child.transform.localPosition = localPosition;
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
            ConfigureRenderer(go.GetComponent<MeshRenderer>(), material);
            return go;
        }

        private static GameObject CreateMeshObject(
            string name, Mesh mesh, Material material, List<GameObject> live)
        {
            var go = new GameObject(name) { layer = 0 };
            live.Add(go);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            ConfigureRenderer(go.AddComponent<MeshRenderer>(), material);
            return go;
        }

        private static void ConfigureRenderer(MeshRenderer renderer, Material material)
        {
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        // ----------------------------------------------------------------- generated assets

        /// <summary>Loads the mesh asset and refills it IN PLACE (GUID kept), or creates it.</summary>
        private static Mesh EnsureMesh(string path, string name, System.Action<Mesh> build)
        {
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            bool created = mesh == null;
            if (created)
            {
                mesh = new Mesh { name = name };
            }

            mesh.Clear();
            build(mesh);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            if (created)
            {
                EnsureFolder(PrefabDir);
                AssetDatabase.CreateAsset(mesh, path);
            }
            else
            {
                EditorUtility.SetDirty(mesh);
            }

            return mesh;
        }

        /// <summary>Open tube, pivot at its bottom centre; UV v runs 0 (bottom) → 1 (top).</summary>
        private static void BuildBandMesh(Mesh mesh)
        {
            int ring = BandSegments + 1; // seam vertex duplicated for the UV wrap
            var vertices = new Vector3[ring * 2];
            var uv = new Vector2[ring * 2];
            var triangles = new int[BandSegments * 6];

            for (int i = 0; i < ring; i++)
            {
                float u = i / (float)BandSegments;
                float angle = u * Mathf.PI * 2f;
                var bottom = new Vector3(Mathf.Cos(angle) * BandRadius, 0f, Mathf.Sin(angle) * BandRadius);
                vertices[i] = bottom;
                vertices[ring + i] = bottom + new Vector3(0f, BandHeight, 0f);
                uv[i] = new Vector2(u, 0f);
                uv[ring + i] = new Vector2(u, 1f);
            }

            for (int i = 0; i < BandSegments; i++)
            {
                int t = i * 6;
                triangles[t] = i;
                triangles[t + 1] = ring + i;
                triangles[t + 2] = i + 1;
                triangles[t + 3] = i + 1;
                triangles[t + 4] = ring + i;
                triangles[t + 5] = ring + i + 1;
            }

            mesh.vertices = vertices;
            mesh.uv = uv;
            mesh.triangles = triangles;
        }

        /// <summary>Two crossed flat arrows (XY and ZY planes) pointing +Y, pivot at the centre.</summary>
        private static void BuildArrowMesh(Mesh mesh)
        {
            float bottom = -ArrowTotalHeight * 0.5f;
            float neck = bottom + ArrowShaftHeight;
            float tip = ArrowTotalHeight * 0.5f;

            // Outline in (across, up): shaft quad 0-3, head triangle 4-6.
            var outline = new[]
            {
                new Vector2(-ArrowShaftHalfWidth, bottom), new Vector2(ArrowShaftHalfWidth, bottom),
                new Vector2(ArrowShaftHalfWidth, neck), new Vector2(-ArrowShaftHalfWidth, neck),
                new Vector2(-ArrowHeadHalfWidth, neck), new Vector2(ArrowHeadHalfWidth, neck),
                new Vector2(0f, tip),
            };
            int[] outlineTriangles = { 0, 2, 1, 0, 3, 2, 4, 6, 5 };

            var vertices = new Vector3[outline.Length * 2];
            var triangles = new int[outlineTriangles.Length * 2];
            for (int i = 0; i < outline.Length; i++)
            {
                vertices[i] = new Vector3(outline[i].x, outline[i].y, 0f);
                vertices[outline.Length + i] = new Vector3(0f, outline[i].y, outline[i].x);
            }

            for (int i = 0; i < outlineTriangles.Length; i++)
            {
                triangles[i] = outlineTriangles[i];
                triangles[outlineTriangles.Length + i] = outlineTriangles[i] + outline.Length;
            }

            mesh.vertices = vertices;
            mesh.triangles = triangles;
        }

        /// <summary>White vertical alpha ramp (opaque at the floor, clear at the top); rewritten IN
        /// PLACE when it exists.</summary>
        private static Texture2D EnsureBandTexture()
        {
            const int width = 4;
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(BandTexturePath);
            bool created = texture == null;
            if (created)
            {
                texture = new Texture2D(width, BandTextureHeight, TextureFormat.RGBA32, false)
                {
                    name = "T_FloorPortalBand",
                };
            }

            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;

            var pixels = new Color[width * BandTextureHeight];
            for (int y = 0; y < BandTextureHeight; y++)
            {
                float v = y / (BandTextureHeight - 1f);
                var color = new Color(1f, 1f, 1f, Mathf.Pow(1f - v, 1.5f));
                for (int x = 0; x < width; x++)
                {
                    pixels[y * width + x] = color;
                }
            }

            texture.SetPixels(pixels);
            texture.Apply(false, false);

            if (created)
            {
                EnsureFolder(MaterialDir);
                AssetDatabase.CreateAsset(texture, BandTexturePath);
            }
            else
            {
                EditorUtility.SetDirty(texture);
            }

            return texture;
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
