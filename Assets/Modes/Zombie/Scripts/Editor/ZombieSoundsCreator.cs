using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace VortexArena.Modes.Zombie.Editor
{
    /// <summary>Creates the <see cref="ZombieSounds"/> asset and fills it from the mode's audio folder so
    /// the mode has sound from the first run.</summary>
    /// <remarks>⚠️ A menu item and not a runtime write (the <see cref="ZombieArsenalCreator"/> pattern):
    /// a build has no <c>AssetDatabase</c>, and an asset born behind the developer's back is an
    /// untracked file that goes missing in the next clone.
    /// <para>An existing asset is only selected, never refilled: the clips are meant to be REPLACED
    /// there, and refilling would undo the swap.</para></remarks>
    internal static class ZombieSoundsCreator
    {
        private const string AssetPath = "Assets/Modes/Zombie/Data/Resources/Zombie/ZombieSounds.asset";

        /// <summary>Every clip, top level of the mode's audio folder.</summary>
        private const string AudioFolder = "Assets/Modes/Zombie/Audio";

        /// <summary>Highest index a numbered pool is scanned to.</summary>
        private const int PoolScanLimit = 16;

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

            // ⚠️ Hit sounds are deliberately absent: SUR_ZombieFlesh / SUR_ZombieHead own them
            // (Zombie_HitFlesh_* · Zombie_HitHead_*), and a second copy here would double every hit.
            // The ambiences and the portal cue are not this asset's fields either.
            FillArray(serialized, "groanClips", Pool("Zombie_Groan"));
            FillArray(serialized, "runnerGroanClips", Pool("Zombie_RunnerGroan"));
            FillArray(serialized, "attackClips", Pool("Zombie_Attack"));
            FillArray(serialized, "deathClips", Pool("Zombie_Death"));
            FillArray(serialized, "spawnClips", Pool("Zombie_Spawn"));
            FillArray(serialized, "screamClips", Pool("Zombie_Scream"));
            FillArray(serialized, "footstepClips", Pool("Zombie_Footstep"));

            FillArray(serialized, "giantRoarClips", Pool("Zombie_GiantRoar"));
            FillArray(serialized, "giantStepClips", Pool("Zombie_GiantStep"));
            FillArray(serialized, "giantSlamClips", Pool("Zombie_GiantSlam"));
            FillArray(serialized, "giantDeathClips", Pool("Zombie_GiantDeath"));

            SetClip(serialized, "waveStartSting", Clip("Zombie_Sting_WaveStart"));
            SetClip(serialized, "waveClearSting", Clip("Zombie_Sting_WaveClear"));
            SetClip(serialized, "finalStandSting", Clip("Zombie_Sting_FinalStand"));
            SetClip(serialized, "victoryClip", Clip("Zombie_Sting_Victory"));
            SetClip(serialized, "defeatClip", Clip("Zombie_Sting_Defeat"));

            // rescueClip is left empty on purpose: there is no separate rescue clip, and the field
            // falls back to the victory one.
            SetClip(serialized, "countdownTick", Clip("Zombie_CountdownTick"));
            SetClip(serialized, "weaponUnlockClip", Clip("Zombie_WeaponUnlock"));
            SetClip(serialized, "reviveProgressClip", Clip("Zombie_ReviveProgress"));
            SetClip(serialized, "reviveCompleteClip", Clip("Zombie_ReviveComplete"));

            SetClip(serialized, "musicTension", Clip("Zombie_Music_Tension"));
            SetClip(serialized, "musicWave", Clip("Zombie_Music_Wave"));

            // Announcement lines (Voiceover channel). The wave-start lines are NOT here: those live
            // in ModeAudioRegistry's zombie rule, since the core plays every RoundStart.
            SetClip(serialized, "waveClearVoice", Clip("Zombie_VO_WaveClear"));
            SetClip(serialized, "newWeaponsVoice", Clip("Zombie_VO_NewWeapons"));
            SetClip(serialized, "finalStandVoice", Clip("Zombie_VO_FinalStand"));
            SetClip(serialized, "giantIncomingVoice", Clip("Zombie_VO_GiantIncoming"));
            SetClip(serialized, "mateDownedVoice", Clip("Zombie_VO_MateDowned"));
            SetClip(serialized, "survivedVoice", Clip("Zombie_VO_Survived"));
            SetClip(serialized, "rescuedVoice", Clip("Zombie_VO_Rescued"));
            SetClip(serialized, "lostVoice", Clip("Zombie_VO_Lost"));

            serialized.ApplyModifiedPropertiesWithoutUndo();

            AssetDatabase.SaveAssets();

            Selection.activeObject = sounds;
            EditorGUIUtility.PingObject(sounds);
            Debug.Log($"[ZombieSounds] Ses verisi üretildi (klipler bağlandı): {AssetPath}");
        }

        /// <summary>Numbered pool (<c>&lt;prefix&gt;_01</c>, <c>_02</c>…) up to the first gap: a new clip
        /// dropped next to the others is picked up without editing this file.</summary>
        private static List<AudioClip> Pool(string prefix)
        {
            var clips = new List<AudioClip>();
            for (int i = 1; i <= PoolScanLimit; i++)
            {
                AudioClip clip = Find($"{prefix}_{i:00}", AudioFolder);
                if (clip == null)
                {
                    break;
                }

                clips.Add(clip);
            }

            if (clips.Count == 0)
            {
                Debug.LogWarning($"[ZombieSounds] Klip bulunamadı: {prefix}_01");
            }

            return clips;
        }

        /// <summary>Final clip from the mode's audio folder; a missing one warns and leaves the field
        /// empty — every one of these hooks is optional at runtime.</summary>
        private static AudioClip Clip(string fileName)
        {
            AudioClip clip = Find(fileName, AudioFolder);
            if (clip == null)
            {
                Debug.LogWarning($"[ZombieSounds] Klip bulunamadı: {fileName}");
            }

            return clip;
        }

        /// <summary>Clip by name in one folder, whatever its extension (mp3 · wav · ogg); <c>null</c>
        /// when it is not there — the caller decides whether that is worth a warning.</summary>
        private static AudioClip Find(string fileName, string folder)
        {
            string[] guids = AssetDatabase.FindAssets($"{fileName} t:AudioClip", new[] { folder });
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);

                // ⚠️ Folder compared exactly, not just searched: the search is recursive, so a clip of
                // the same name in a subfolder would win over the one that belongs here.
                if (string.Equals(Path.GetFileNameWithoutExtension(path), fileName, System.StringComparison.Ordinal)
                    && string.Equals(Path.GetDirectoryName(path).Replace('\\', '/'), folder, System.StringComparison.Ordinal))
                {
                    return AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                }
            }

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
