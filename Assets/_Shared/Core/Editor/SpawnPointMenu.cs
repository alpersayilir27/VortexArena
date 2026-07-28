using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VortexArena.Core.Arena;

namespace VortexArena.Core.Editor
{
    /// <summary>
    /// Hiyerarşi sağ-tık menüsü: <c>GameObject &gt; VortexArena &gt; Spawn Point</c>.
    /// Arenanın <b>tek</b> başlangıç noktasını (<see cref="SpawnPoint"/>) üretir; yerleştirme
    /// ELLE yapılır (sihirbaz ya da başka bir araç bu noktayı otomatik koymaz).
    /// <para>
    /// Sahnede zaten bir nokta varsa uyarı basılır ama üretim ENGELLENMEZ — kullanıcı eskisini
    /// silip yenisini koymak isteyebilir; engellemek "önce sil, sonra ekle" sırasını dayatırdı.
    /// </para>
    /// </summary>
    internal static class SpawnPointMenu
    {
        private const string MENU_PATH = "GameObject/VortexArena/Spawn Point";

        [MenuItem(MENU_PATH, false, 32)]
        private static void CreateSpawnPoint(MenuCommand command)
        {
            // Hiyerarşi bağlam menüsünde Unity bu komutu SEÇİLEN HER OBJE için bir kez çağırır;
            // tek bir nokta ürettiğimiz için yalnız ilk çağrıyı geçiriyoruz.
            GameObject[] selection = Selection.gameObjects;
            if (command.context != null && selection.Length > 1 && command.context != selection[0])
            {
                return;
            }

            // ⚠️ Sayım sahneden yapılır, SpawnPoint.All'dan DEĞİL: o kayıt OnEnable'da dolar ve
            // OnEnable edit kipinde çalışmaz ([ExecuteAlways] yok) — kayıt burada hep boş görünür.
            int existing = Object.FindObjectsByType<SpawnPoint>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;

            var go = new GameObject("SpawnPoint", typeof(SpawnPoint));

            // Seçili sahne objesinin altına (Unity'nin standart davranışı); prefab asset'i
            // seçiliyse köke düşer, çünkü SetParentAndAlign yalnız sahne objesi bekler.
            GameObject context = command.context as GameObject;
            GameObject parent = context != null && !EditorUtility.IsPersistent(context) && context.scene.IsValid()
                ? context
                : null;
            GameObjectUtility.SetParentAndAlign(go, parent);

            Undo.RegisterCreatedObjectUndo(go, "VortexArena Spawn Point");
            Selection.activeGameObject = go;
            EditorSceneManager.MarkSceneDirty(go.scene);

            if (existing > 0)
            {
                Debug.LogWarning(
                    $"[SpawnPoint] Sahnede zaten {existing} başlangıç noktası vardı — arena başına " +
                    "TEK nokta beklenir. Fazlalığı sil.", go);
                return;
            }

            Debug.Log("[SpawnPoint] Başlangıç noktası oluşturuldu — arenadaki yerine ELLE taşı " +
                      "(hiçbir kod oyuncuyu buraya ışınlamaz, yalnız göstergedir).", go);
        }
    }
}
