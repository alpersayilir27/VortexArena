using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace VortexArena.Core.Editor
{
    /// <summary>Applies a Blender-exported scene rules JSON (<c>&lt;Art&gt;/*_Scene.json</c>) to the active scene.</summary>
    /// <remarks>
    /// Blender writes both JSON files (pipeline: <c>blender/&lt;Proje&gt;/README.md</c>); this tool only reads them, so
    /// the same art can be dressed identically in every venue scene with no hand work. Every step is idempotent:
    /// models, lights, FX, reflection probe and the per-object static/shadow/lightmap/nav rules converge to the
    /// same scene on re-run. The tool never saves the scene and never bakes — lighting and NavMesh bakes are
    /// separate steps.
    /// </remarks>
    public static class BlenderSceneDresser
    {
        private const string Tag = "[BlenderScene] ";

        private const StaticEditorFlags BaseStaticFlags = StaticEditorFlags.BatchingStatic
                                                        | StaticEditorFlags.OccludeeStatic
                                                        | StaticEditorFlags.OccluderStatic
                                                        | StaticEditorFlags.ReflectionProbeStatic;

        [MenuItem("Tools/VortexArena/Art/Blender Sahnesini Diz (seçili Art klasörü)", false, 41)]
        private static void DressSelected()
        {
            string folder = AssetDatabase.GetAssetPath(Selection.activeObject);
            if (string.IsNullOrEmpty(folder) || !AssetDatabase.IsValidFolder(folder))
            {
                Debug.LogError(Tag + "Project penceresinde Blender çıktısının Art klasörünü seçin.");
                return;
            }

            Dress(folder);
        }

        /// <summary>Dresses the active scene from one art folder; returns a one-line summary.</summary>
        public static string Dress(string artFolder)
        {
            string scenePath = Directory.GetFiles(artFolder, "*_Scene.json").FirstOrDefault();
            if (scenePath == null)
            {
                Debug.LogError(Tag + $"{artFolder} içinde *_Scene.json yok.");
                return "no json";
            }

            JObject rules = JObject.Parse(File.ReadAllText(scenePath));
            Scene scene = SceneManager.GetActiveScene();
            int warnings = 0;

            GameObject artRoot = EnsureRoot(scene, (string)rules["root"]);
            var objects = rules["objects"] as JObject ?? new JObject();
            int models = DressModels(scene, artRoot, artFolder + "/Models", objects);
            int lights = DressLights(scene, artFolder, rules, ref warnings);
            int fx = DressFx(scene, rules["fx"] as JArray, ref warnings);
            bool probe = DressReflectionProbe(scene, rules["reflectionProbe"] as JObject);

            EditorSceneManager.MarkSceneDirty(scene);
            string summary = $"{models} model, {lights} ışık, {fx} FX" + (probe ? ", yansıma probu dizildi" : " dizildi");
            if (warnings > 0)
            {
                summary += $" ({warnings} uyarı)";
            }

            Debug.Log(Tag + summary + ".");
            return summary;
        }

        /// <summary>Finds a root-level GameObject by name, creating it at the origin when missing.</summary>
        private static GameObject EnsureRoot(Scene scene, string name)
        {
            GameObject go = scene.GetRootGameObjects().FirstOrDefault(g => g.name == name);
            if (go != null)
            {
                return go;                                                     // existing roots carry components (NavMeshSurface)
            }

            go = new GameObject(name);
            SceneManager.MoveGameObjectToScene(go, scene);
            return go;
        }

        private static int DressModels(Scene scene, GameObject artRoot, string modelDir, JObject objects)
        {
            // Dropped from the Blender export: the FBX is gone, so the instance can no longer be reconciled.
            foreach (Transform child in artRoot.transform.Cast<Transform>().ToList())
            {
                if (PrefabUtility.GetPrefabInstanceStatus(child.gameObject) == PrefabInstanceStatus.MissingAsset)
                {
                    Object.DestroyImmediate(child.gameObject);
                }
            }

            int count = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { modelDir }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (model == null)
                {
                    continue;
                }

                GameObject instance = EnsureModelInstance(scene, artRoot, model);
                ApplyObjectRules(instance, objects[instance.name] as JObject);
                count++;
            }

            return count;
        }

        /// <summary>Ensures a direct child of the art root that is a prefab instance of the model.</summary>
        private static GameObject EnsureModelInstance(Scene scene, GameObject artRoot, GameObject model)
        {
            Transform existing = artRoot.transform.Cast<Transform>().FirstOrDefault(t => t.name == model.name);
            if (existing != null && PrefabUtility.GetCorrespondingObjectFromSource(existing.gameObject) != model)
            {
                Object.DestroyImmediate(existing.gameObject);                  // stale: points at another asset
                existing = null;
            }

            if (existing == null)
            {
                var created = (GameObject)PrefabUtility.InstantiatePrefab(model, scene);
                created.name = model.name;
                existing = created.transform;
                existing.SetParent(artRoot.transform, false);
            }

            existing.localPosition = Vector3.zero;
            existing.localRotation = Quaternion.identity;
            existing.localScale = Vector3.one;
            return existing.gameObject;
        }

        private static void ApplyObjectRules(GameObject go, JObject rule)
        {
            bool shadows = (bool?)rule?["shadows"] ?? true;
            bool gi = (bool?)rule?["gi"] ?? true;
            float lightmap = (float?)rule?["lightmap"] ?? 1f;
            string nav = (string)rule?["nav"] ?? "include";

            StaticEditorFlags flags = gi ? BaseStaticFlags | StaticEditorFlags.ContributeGI : BaseStaticFlags;
            foreach (Transform t in go.GetComponentsInChildren<Transform>(true))
            {
                GameObjectUtility.SetStaticEditorFlags(t.gameObject, flags);
            }

            foreach (MeshRenderer mr in go.GetComponentsInChildren<MeshRenderer>(true))
            {
                mr.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
                mr.receiveGI = gi ? ReceiveGI.Lightmaps : ReceiveGI.LightProbes;
                mr.lightProbeUsage = LightProbeUsage.BlendProbes;
                SetFloatProperty(mr, "m_ScaleInLightmap", lightmap);
            }

            ApplyNavRule(go, nav);
        }

        private static void ApplyNavRule(GameObject go, string nav)
        {
            var modifier = go.GetComponent<NavMeshModifier>();
            if (nav == "ignore")
            {
                if (modifier == null)
                {
                    modifier = go.AddComponent<NavMeshModifier>();
                }

                modifier.ignoreFromBuild = true;
                modifier.applyToChildren = true;
            }
            else if (modifier != null)
            {
                Object.DestroyImmediate(modifier);
            }
        }

        private static int DressLights(Scene scene, string artFolder, JObject rules, ref int warnings)
        {
            string lightsPath = Directory.GetFiles(artFolder, "*_Lights.json").FirstOrDefault();
            if (lightsPath == null)
            {
                Debug.LogWarning(Tag + $"{artFolder} içinde *_Lights.json yok, ışıklar atlandı.");
                warnings++;
                return 0;
            }

            GameObject lightsRoot = EnsureRoot(scene, (string)rules["lightsRoot"]);
            foreach (Transform child in lightsRoot.transform.Cast<Transform>().ToList())
            {
                Object.DestroyImmediate(child.gameObject);                     // lights are cheap: rebuilt wholesale
            }

            var kinds = rules["lightKinds"] as JObject ?? new JObject();
            var halo = AssetDatabase.LoadAssetAtPath<GameObject>((string)rules["haloPrefab"]);
            var entries = JObject.Parse(File.ReadAllText(lightsPath))["lights"] as JArray ?? new JArray();
            foreach (JToken token in entries)
            {
                BuildLight(lightsRoot, (JObject)token, kinds, halo, ref warnings);
            }

            return entries.Count;
        }

        private static void BuildLight(GameObject parent, JObject entry, JObject kinds, GameObject halo, ref int warnings)
        {
            string kind = (string)entry["kind"];
            var k = kinds[kind] as JObject;
            if (k == null)
            {
                Debug.LogWarning(Tag + $"Bilinmeyen ışık türü '{kind}', varsayılan değerler kullanıldı.");
                warnings++;
            }

            var go = new GameObject((string)entry["name"]);
            go.transform.SetParent(parent.transform, false);
            go.transform.localPosition = ReadVector(entry["position"]);

            Light light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.lightmapBakeType = LightmapBakeType.Baked;
            light.color = ReadColor(entry["color"], 1f);
            light.intensity = (float?)k?["intensity"] ?? 1f;
            light.range = (float?)k?["range"] ?? 5f;
            light.shadows = LightShadows.Soft;
            SetFloatProperty(light, "m_ShadowRadius", 0.05f);                  // baked soft shadow penumbra

            float haloSize = (float?)k?["halo"] ?? 0f;
            if (halo != null && haloSize > 0f)
            {
                AttachHalo(go, halo, haloSize, light.color);
            }
        }

        private static void AttachHalo(GameObject lightGo, GameObject haloPrefab, float size, Color color)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(haloPrefab, lightGo.transform);
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;

            var ps = instance.GetComponent<ParticleSystem>();
            if (ps == null)
            {
                return;
            }

            ParticleSystem.MainModule main = ps.main;
            main.startSize = new ParticleSystem.MinMaxCurve(size * 0.85f, size);
            // Keep the prefab's authored alpha: only the hue follows the light.
            main.startColor = new Color(color.r, color.g, color.b, main.startColor.color.a);
        }

        private static int DressFx(Scene scene, JArray fx, ref int warnings)
        {
            if (fx == null)
            {
                return 0;
            }

            int count = 0;
            foreach (JToken token in fx)
            {
                string path = (string)token["prefab"];
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null)
                {
                    Debug.LogWarning(Tag + $"FX prefab'ı bulunamadı: {path}");
                    warnings++;
                    continue;
                }

                GameObject instance = scene.GetRootGameObjects()
                    .FirstOrDefault(g => PrefabUtility.GetCorrespondingObjectFromSource(g) == prefab);
                if (instance == null)
                {
                    instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                }

                instance.transform.SetPositionAndRotation(ReadVector(token["position"]), Quaternion.identity);
                ApplyFxRing(instance, token["ring"] as JArray);
                count++;
            }

            return count;
        }

        /// <summary>Fits a Circle-shaped emitter to the arena ring (inner/outer radius).</summary>
        private static void ApplyFxRing(GameObject instance, JArray ring)
        {
            var ps = instance.GetComponent<ParticleSystem>();
            if (ring == null || ring.Count < 2 || ps == null)
            {
                return;
            }

            ParticleSystem.ShapeModule shape = ps.shape;
            if (shape.shapeType != ParticleSystemShapeType.Circle)
            {
                return;
            }

            float inner = (float)ring[0], outer = (float)ring[1];
            shape.radius = outer;
            shape.radiusThickness = outer > 0f ? (outer - inner) / outer : 1f;
        }

        private static bool DressReflectionProbe(Scene scene, JObject spec)
        {
            if (spec == null)
            {
                return false;
            }

            GameObject go = EnsureRoot(scene, (string)spec["name"]);
            go.transform.position = ReadVector(spec["center"]);
            var probe = go.GetComponent<ReflectionProbe>();
            if (probe == null)
            {
                probe = go.AddComponent<ReflectionProbe>();                    // no ??: editor GetComponent returns fake-null
            }

            probe.mode = ReflectionProbeMode.Baked;
            probe.size = ReadVector(spec["size"]);
            probe.center = Vector3.zero;
            probe.resolution = 128;
            probe.hdr = false;
            probe.boxProjection = false;
            probe.importance = 1;
            return true;
        }

        /// <summary>Writes a serialized float that has no reliable public setter in the editor.</summary>
        private static void SetFloatProperty(Object target, string path, float value)
        {
            var so = new SerializedObject(target);
            SerializedProperty p = so.FindProperty(path);
            if (p == null)
            {
                return;
            }

            p.floatValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static Vector3 ReadVector(JToken token)
        {
            var a = (JArray)token;
            return new Vector3((float)a[0], (float)a[1], (float)a[2]);
        }

        private static Color ReadColor(JToken token, float alpha)
        {
            var a = (JArray)token;
            return new Color((float)a[0], (float)a[1], (float)a[2], alpha);
        }
    }
}
