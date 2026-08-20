using UnityEditor;
using UnityEngine;
using VortexArena.Core.Audio;

namespace VortexArena.Core.Editor
{
    /// <summary>Selects and pings <c>ModeAudioRegistry</c>, which no scene references.</summary>
    /// <remarks>Editing happens in the Inspector; a second editing surface is deliberately not
    /// opened (describing one rule in two places lets them drift apart silently).
    /// Creates the asset if missing but never the folder: a missing <c>Resources</c> folder is the
    /// real problem and silently recreating it would hide that.</remarks>
    internal static class ModeAudioRegistryMenu
    {
        private const string MENU_PATH = "Tools/VortexArena/Audio/Mod Sesleri";
        private const string FOLDER = "Assets/_Shared/Data/Resources";
        private const string ASSET_PATH = FOLDER + "/" + ModeAudioRegistry.ResourceName + ".asset";

        [MenuItem(MENU_PATH, false, 60)]
        private static void OpenRegistry()
        {
            var registry = AssetDatabase.LoadAssetAtPath<ModeAudioRegistry>(ASSET_PATH);
            if (registry == null)
            {
                registry = CreateRegistry();
                if (registry == null)
                {
                    return;
                }
            }

            Selection.activeObject = registry;
            EditorGUIUtility.PingObject(registry);
        }

        private static ModeAudioRegistry CreateRegistry()
        {
            if (!AssetDatabase.IsValidFolder(FOLDER))
            {
                Debug.LogError(
                    $"[ModeAudioRegistry] '{FOLDER}' klasörü yok. Kayıt {ModeAudioRegistry.ResourceName} " +
                    "adıyla ve BU klasörde durmalı (çalışma anında Resources.Load ile alınıyor).");
                return null;
            }

            var registry = ScriptableObject.CreateInstance<ModeAudioRegistry>();
            AssetDatabase.CreateAsset(registry, ASSET_PATH);
            AssetDatabase.SaveAssets();

            Debug.Log($"[ModeAudioRegistry] Kayıt bulunamadı, boş olarak oluşturuldu: {ASSET_PATH}");
            return registry;
        }
    }
}
