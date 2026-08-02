using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace VortexArena.Net.Editor
{
    /// <summary>
    /// sceneId bekçisi: her sahne KAYDINDA 0 kalan ve ÇAKIŞAN
    /// <see cref="NetIdentity.SceneId"/> değerlerini onarır — Mirror'ın sceneId bake
    /// deseninin sade hali.
    /// <para>
    /// Asıl derdi SAHNE KOPYALAMA: elle "Save As" ya da bir sahnenin çoğaltılması
    /// ile üretilen kopyada tüm id'ler kaynak sahneyle birebir aynı gelir. Bekçi kayıt anında
    /// çakışmaları ayırdığı için kimse elle id yönetmez, kopyalanan arena kutudan çıkar çıkmaz
    /// tutarlıdır.
    /// </para>
    /// Sahnede hiç NetIdentity yoksa (bugünkü sahnelerin çoğu) tamamen sessizdir.
    /// </summary>
    [InitializeOnLoad]
    internal static class SceneIdGuard
    {
        static SceneIdGuard()
        {
            // Statik ctor domain reload dışında da yeniden koşabildiği için önce söküyoruz:
            // çift abonelik = çift onarım + çift log.
            EditorSceneManager.sceneSaving -= HandleSceneSaving;
            EditorSceneManager.sceneSaving += HandleSceneSaving;
        }

        private static void HandleSceneSaving(Scene scene, string path)
        {
            if (SceneIdUtility.RepairScene(scene, out int fixedCount))
            {
                Debug.Log($"[VortexArena] '{scene.name}': {fixedCount} NetIdentity sceneId onarıldı (0/çakışma).");
            }
        }
    }
}
