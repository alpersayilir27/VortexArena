using System.IO;
using UnityEditor;
using UnityEngine;

namespace VortexArena.Modes.Zombie.Editor
{
    /// <summary>Creates the <see cref="ZombieArsenal"/> asset so the unlock table can be edited in the
    /// Inspector.</summary>
    /// <remarks>⚠️ A menu item and not a lazy "create it when first needed": a runtime
    /// <c>AssetDatabase</c> write does not exist in a build, and an asset born behind the developer's
    /// back is an untracked file that goes missing in the next clone. Without the asset the mode runs on
    /// <see cref="ZombieArsenal.DefaultEntries"/>.</remarks>
    internal static class ZombieArsenalCreator
    {
        private const string AssetPath = "Assets/Modes/Zombie/Data/Resources/Zombie/ZombieArsenal.asset";

        [MenuItem("Tools/VortexArena/Zombie/Cephanelik tablosunu oluştur")]
        private static void Create()
        {
            var existing = AssetDatabase.LoadAssetAtPath<ZombieArsenal>(AssetPath);
            if (existing != null)
            {
                Selection.activeObject = existing;
                EditorGUIUtility.PingObject(existing);
                Debug.Log($"[ZombieArsenal] Tablo zaten var: {AssetPath}");
                return;
            }

            EnsureFolder(Path.GetDirectoryName(AssetPath).Replace('\\', '/'));

            ZombieArsenal arsenal = ScriptableObject.CreateInstance<ZombieArsenal>();
            AssetDatabase.CreateAsset(arsenal, AssetPath);
            AssetDatabase.SaveAssets();

            Selection.activeObject = arsenal;
            EditorGUIUtility.PingObject(arsenal);
            Debug.Log($"[ZombieArsenal] Tablo üretildi: {AssetPath}");
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
