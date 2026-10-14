using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace VortexArena.Modes.Zombie.Editor
{
    /// <summary>Creates the <see cref="ZombieSounds"/> asset and fills it from the placeholder folder so
    /// the mode has sound from the first run.</summary>
    /// <remarks>⚠️ A menu item and not a runtime write (the <see cref="ZombieArsenalCreator"/> pattern):
    /// a build has no <c>AssetDatabase</c>, and an asset born behind the developer's back is an
    /// untracked file that goes missing in the next clone.
    /// <para>An existing asset is only selected, never refilled: the clips are meant to be REPLACED
    /// there, and refilling would undo the swap.</para></remarks>
    internal static class ZombieSoundsCreator
    {
        private const string AssetPath = "Assets/Modes/Zombie/Data/Resources/Zombie/ZombieSounds.asset";

        private const string PlaceholderFolder = "Assets/Modes/Zombie/Audio/Placeholder";

        [MenuItem("Tools/VortexArena/Zombie/Ses verisini oluştur")]
        private static void Create()
        {
            var existing = AssetDatabase.LoadAssetAtPath<ZombieSounds>(AssetPath);
            if (existing != null)
            {
                Selection.activeObject = existing;
                EditorGUIUtility.PingObject(existing);
                Debug.Log($"[ZombieSounds] Ses verisi zaten var: {AssetPath}");
                return;
            }

            EnsureFolder(Path.GetDirectoryName(AssetPath).Replace('\\', '/'));

            ZombieSounds sounds = ScriptableObject.CreateInstance<ZombieSounds>();
            AssetDatabase.CreateAsset(sounds, AssetPath);

            var serialized = new SerializedObject(sounds);

            // Groan pool stops at 06: the last two are the spawn voice, and a clip in both pools would
            // make an appearing zombie sound like it is already idling.
            FillArray(serialized, "groanClips", Clips("Zombie_Groan_", 1, 6));
            FillArray(serialized, "attackClips", Clips("Zombie_Attack_", 1, 7));
            FillArray(serialized, "deathClips", Clips("Zombie_Death_", 1, 4));
            FillArray(serialized, "spawnClips", Clips("Zombie_Groan_", 7, 8));

            // Same two clips as the spawn voice: those are the only shouted placeholders. The scream
            // pool stays FILLED anyway, so swapping in a real cry is a drop into this one field.
            FillArray(serialized, "screamClips", Clips("Zombie_Groan_", 7, 8));

            SetClip(serialized, "musicTension", Clip("Zombie_Music_Tension"));
            SetClip(serialized, "musicWave", Clip("Zombie_Music_Wave"));

            serialized.ApplyModifiedPropertiesWithoutUndo();

            AssetDatabase.SaveAssets();

            Selection.activeObject = sounds;
            EditorGUIUtility.PingObject(sounds);
            Debug.Log($"[ZombieSounds] Ses verisi üretildi (yer tutucu klipler bağlandı): {AssetPath}");
        }

        private static List<AudioClip> Clips(string prefix, int from, int to)
        {
            var clips = new List<AudioClip>();
            for (int i = from; i <= to; i++)
            {
                AudioClip clip = Clip($"{prefix}{i:00}");
                if (clip != null)
                {
                    clips.Add(clip);
                }
            }

            return clips;
        }

        /// <summary>Clip by name, whatever its extension (wav · ogg).</summary>
        private static AudioClip Clip(string fileName)
        {
            string[] guids = AssetDatabase.FindAssets($"{fileName} t:AudioClip", new[] { PlaceholderFolder });
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (string.Equals(Path.GetFileNameWithoutExtension(path), fileName, System.StringComparison.Ordinal))
                {
                    return AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                }
            }

            Debug.LogWarning($"[ZombieSounds] Yer tutucu klip bulunamadı: {fileName}");
            return null;
        }

        private static void FillArray(SerializedObject serialized, string field, List<AudioClip> clips)
        {
            SerializedProperty property = serialized.FindProperty(field);
            if (property == null)
            {
                return;
            }

            property.arraySize = clips.Count;
            for (int i = 0; i < clips.Count; i++)
            {
                property.GetArrayElementAtIndex(i).objectReferenceValue = clips[i];
            }
        }

        private static void SetClip(SerializedObject serialized, string field, AudioClip clip)
        {
            SerializedProperty property = serialized.FindProperty(field);
            if (property != null)
            {
                property.objectReferenceValue = clip;
            }
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
