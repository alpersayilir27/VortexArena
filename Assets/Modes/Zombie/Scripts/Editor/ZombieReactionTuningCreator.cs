using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace VortexArena.Modes.Zombie.Editor
{
    /// <summary>Creates the <see cref="ZombieReactionTuning"/> asset and wires what already exists in
    /// the project into it: the gore models, the two gore materials, the blood decal material and the
    /// impact prefabs.</summary>
    /// <remarks>⚠️ A menu item and not a lazy "create it when first needed": a runtime
    /// <c>AssetDatabase</c> write does not exist in a build, and an asset born behind the developer's
    /// back is an untracked file that goes missing in the next clone. Without the asset the reaction
    /// runs on the built-in defaults, with no gore meshes and no sounds.
    /// <para>Re-running it only FILLS EMPTY slots: a tuned number or a hand-picked clip is never
    /// overwritten.</para></remarks>
    internal static class ZombieReactionTuningCreator
    {
        private const string AssetPath =
            "Assets/Modes/Zombie/Data/Resources/Zombie/ZombieReactionTuning.asset";

        private const string GoreDir = "Assets/Modes/Zombie/Art/Gore";
        private const string MaterialDir = "Assets/Modes/Zombie/Materials";

        private const string FleshMaterialPath = MaterialDir + "/M_ZG_Flesh.mat";
        private const string BoneMaterialPath = MaterialDir + "/M_ZG_Bone.mat";
        private const string DecalMaterialPath = MaterialDir + "/M_ZG_BloodDecal.mat";

        private const string HeadBurstPath = "Assets/_Shared/FX/FX_Impact_ZombieHead.prefab";
        private const string SeverBurstPath = "Assets/_Shared/FX/FX_Impact_ZombieBlood.prefab";

        [MenuItem("Tools/VortexArena/Zombie/Tepki ayarlarını oluştur")]
        private static void Create()
        {
            var tuning = AssetDatabase.LoadAssetAtPath<ZombieReactionTuning>(AssetPath);
            bool fresh = tuning == null;

            if (fresh)
            {
                EnsureFolder(Path.GetDirectoryName(AssetPath).Replace('\\', '/'));
                tuning = ScriptableObject.CreateInstance<ZombieReactionTuning>();
                AssetDatabase.CreateAsset(tuning, AssetPath);
            }

            var so = new SerializedObject(tuning);

            SetIfEmpty(so, "fleshMaterial", BuildMaterial(FleshMaterialPath, "Universal Render Pipeline/Lit",
                new Color(0.32f, 0.045f, 0.04f), false));
            SetIfEmpty(so, "boneMaterial", BuildMaterial(BoneMaterialPath, "Universal Render Pipeline/Lit",
                new Color(0.82f, 0.78f, 0.68f), false));
            SetIfEmpty(so, "bloodDecalMaterial", BuildMaterial(DecalMaterialPath,
                "Universal Render Pipeline/Unlit", Color.white, true));

            SetIfEmpty(so, "neckStumpPrefab", LoadGore("ZG_Stump_Neck"));
            SetIfEmpty(so, "limbStumpPrefab", LoadGore("ZG_Stump_Limb"));

            SetArrayIfEmpty(so, "fleshGibPrefabs", "ZG_Gib_Flesh_A", "ZG_Gib_Flesh_B", "ZG_Gib_Flesh_C");
            SetArrayIfEmpty(so, "boneGibPrefabs", "ZG_Gib_Bone_A");
            SetArrayIfEmpty(so, "skullGibPrefabs", "ZG_Gib_Skull_A", "ZG_Gib_Skull_B");

            SetIfEmpty(so, "headBurstPrefab", AssetDatabase.LoadAssetAtPath<GameObject>(HeadBurstPath));
            SetIfEmpty(so, "severBurstPrefab", AssetDatabase.LoadAssetAtPath<GameObject>(SeverBurstPath));

            so.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();

            Selection.activeObject = tuning;
            EditorGUIUtility.PingObject(tuning);
            Debug.Log($"[ZombieReactionTuning] {(fresh ? "Üretildi" : "Boş yuvalar dolduruldu")}: {AssetPath}");
        }

        private static GameObject LoadGore(string name) =>
            AssetDatabase.LoadAssetAtPath<GameObject>($"{GoreDir}/{name}.fbx");

        private static void SetIfEmpty(SerializedObject so, string field, Object value)
        {
            SerializedProperty property = so.FindProperty(field);
            if (property != null && value != null && property.objectReferenceValue == null)
            {
                property.objectReferenceValue = value;
            }
        }

        private static void SetArrayIfEmpty(SerializedObject so, string field, params string[] names)
        {
            SerializedProperty property = so.FindProperty(field);
            if (property == null || property.arraySize > 0)
            {
                return;
            }

            for (int i = 0; i < names.Length; i++)
            {
                GameObject model = LoadGore(names[i]);
                if (model == null)
                {
                    continue;
                }

                property.arraySize++;
                property.GetArrayElementAtIndex(property.arraySize - 1).objectReferenceValue = model;
            }
        }

        /// <summary>Loads the material, or creates it. ⚠️ The transparent variant needs the blend state
        /// AND the keyword: URP reads the surface type from both, and a material with only the keyword
        /// renders opaque in the player.</summary>
        private static Material BuildMaterial(string path, string shaderName, Color colour, bool transparent)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
            {
                return existing;
            }

            Shader shader = Shader.Find(shaderName);
            if (shader == null)
            {
                Debug.LogWarning($"[ZombieReactionTuning] '{shaderName}' shader'ı bulunamadı — " +
                                 $"{Path.GetFileName(path)} üretilemedi.");
                return null;
            }

            EnsureFolder(MaterialDir);

            var material = new Material(shader) { color = colour };

            if (transparent)
            {
                material.SetFloat("_Surface", 1f);
                material.SetFloat("_Blend", 0f);
                material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                material.SetFloat("_ZWrite", 0f);
                material.SetFloat("_Cull", (float)CullMode.Off);
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.renderQueue = (int)RenderQueue.Transparent;
            }

            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder))
            {
                return;
            }

            string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }
    }
}
