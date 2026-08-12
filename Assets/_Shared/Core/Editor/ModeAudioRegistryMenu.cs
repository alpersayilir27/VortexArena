using UnityEditor;
using UnityEngine;
using VortexArena.Core.Audio;

namespace VortexArena.Core.Editor
{
    /// <summary>
    /// Menü kısayolu: <c>Tools &gt; VortexArena &gt; Audio &gt; Mod Sesleri</c>. Kaydın hiçbir
    /// sahneden referansı olmadığı için Project penceresinde aranması gerekirdi; bu menü onu bulur,
    /// seçer ve işaretler — <b>düzenleme yeri Inspector'dır</b> (ikinci bir düzenleme yüzeyi
    /// açılmaması bilinçli: aynı kuralı iki yerde tarif etmek ikisinin sessizce sapması demektir).
    /// <para>
    /// Asset yoksa aynı yolda oluşturulur. Klasör üretilmez: <c>Resources</c> klasörü repoda
    /// duruyor, yoksa asıl sorun kaybolmuş bir klasördür ve sessizce yenisini açmak onu gizler.
    /// </para>
    /// </summary>
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
