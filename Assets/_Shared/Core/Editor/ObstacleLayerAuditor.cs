using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using VortexArena.Core.Arena;

namespace VortexArena.Core.Editor
{
    /// <summary>
    /// Açık sahnedeki <c>Obstacle</c> layer'ını denetler: engel ihlali sisteminin çalışması için
    /// gereken tek kurulum kuralı <b>collider'ın konveks olmasıdır</b> ve bu kural gözle
    /// denetlenemez.
    /// <para>
    /// <b>Neden bir araç gerekiyor:</b> non-convex bir <c>MeshCollider</c> bu layer'a girdiğinde
    /// <c>Collider.ClosestPoint</c> girdi noktasını aynen döndürür → nokta-içeride testi her zaman
    /// "içeride" der → <b>o sahnedeki herkes anında ölmeye başlar</b>. Çalışma anında
    /// <c>BodyViolationProbe</c> böyle bir collider'ı eleyip hata basıyor, ama o satır ancak biri
    /// Play'e girip konsola bakınca görülür. Bu araç aynı soruyu <b>sahne kaydedilmeden önce</b>
    /// sorar.
    /// </para>
    /// <para>
    /// ⚠️ Araç hiçbir şeyi DÜZELTMEZ, yalnız raporlar. Bir collider'ı otomatik convex işaretlemek
    /// ya da layer'ını değiştirmek, sanatçının bilerek yaptığı bir seçimi sessizce ezmek olurdu.
    /// </para>
    /// </summary>
    public static class ObstacleLayerAuditor
    {
        private const string MenuPath = "Tools/VortexArena/Arena/Engel Hacimlerini Denetle";

        [MenuItem(MenuPath)]
        public static void Audit()
        {
            int layer = LayerMask.NameToLayer(ArenaLayers.ObstacleName);
            if (layer < 0)
            {
                EditorUtility.DisplayDialog(
                    "Engel hacimleri",
                    $"'{ArenaLayers.ObstacleName}' layer'ı projede tanımlı değil.\n\n" +
                    "Project Settings > Tags and Layers altında bu adla bir user layer açılmadan " +
                    "engel ihlali tespiti hiç çalışmaz.",
                    "Tamam");
                return;
            }

            var nonConvex = new List<GameObject>();
            var triggers = new List<GameObject>();
            var colliderless = new List<GameObject>();
            int healthy = 0;

            foreach (GameObject root in EditorSceneRoots())
            {
                Transform[] all = root.GetComponentsInChildren<Transform>(true);
                for (int i = 0; i < all.Length; i++)
                {
                    GameObject go = all[i].gameObject;
                    if (go.layer != layer)
                    {
                        continue;
                    }

                    var collider = go.GetComponent<Collider>();
                    if (collider == null)
                    {
                        // Layer'ı damgalanmış ama collider'ı olmayan obje: çoğu zaman layer'ın
                        // çocuklara da uygulandığı (görsel mesh'ler) durumdur — zararsız ama
                        // yazımcının niyetini bilmek için raporlanır.
                        colliderless.Add(go);
                        continue;
                    }

                    if (collider.isTrigger)
                    {
                        triggers.Add(go);
                        continue;
                    }

                    if (collider is MeshCollider mesh && !mesh.convex)
                    {
                        nonConvex.Add(go);
                        continue;
                    }

                    healthy++;
                }
            }

            Report(nonConvex, triggers, colliderless, healthy);
        }

        private static void Report(List<GameObject> nonConvex, List<GameObject> triggers,
            List<GameObject> colliderless, int healthy)
        {
            var text = new StringBuilder();
            text.AppendLine($"Sağlam engel hacmi: {healthy}");

            if (nonConvex.Count > 0)
            {
                text.AppendLine();
                text.AppendLine($"⛔ KONVEKS DEĞİL ({nonConvex.Count}) — bu objeler engel ihlali");
                text.AppendLine("hesabından ÇIKARILIR (çalışma anında da elenirler).");
                text.AppendLine("MeshCollider > Convex işaretle ya da kaba bir Box/Capsule kullan:");
                AppendNames(text, nonConvex);
            }

            if (triggers.Count > 0)
            {
                text.AppendLine();
                text.AppendLine($"⚠️ TRIGGER ({triggers.Count}) — engel hacmi katı olmalı; trigger");
                text.AppendLine("collider hem mermiyi durdurur hem tespitte yok sayılır:");
                AppendNames(text, triggers);
            }

            if (colliderless.Count > 0)
            {
                text.AppendLine();
                text.AppendLine($"ℹ️ Collider'sız ({colliderless.Count}) — layer damgalı ama collider yok;");
                text.AppendLine("bu objeler tespitte hiç görünmez:");
                AppendNames(text, colliderless);
            }

            if (nonConvex.Count == 0 && triggers.Count == 0 && colliderless.Count == 0)
            {
                text.AppendLine();
                text.AppendLine("Sorun bulunmadı.");
            }

            // Konsola da yazılır: dialog kapanınca liste kaybolmasın, objeye tıklanabilsin.
            for (int i = 0; i < nonConvex.Count; i++)
            {
                Debug.LogError($"[Engel denetimi] '{Path(nonConvex[i])}' konveks değil — " +
                               "engel ihlali hesabından çıkarıldı.", nonConvex[i]);
            }

            for (int i = 0; i < triggers.Count; i++)
            {
                Debug.LogWarning($"[Engel denetimi] '{Path(triggers[i])}' trigger — engel hacmi " +
                                 "katı olmalı.", triggers[i]);
            }

            EditorUtility.DisplayDialog("Engel hacimleri", text.ToString(), "Tamam");
        }

        private static void AppendNames(StringBuilder text, List<GameObject> objects)
        {
            const int MaxListed = 12;
            for (int i = 0; i < objects.Count && i < MaxListed; i++)
            {
                text.AppendLine($"  • {Path(objects[i])}");
            }

            if (objects.Count > MaxListed)
            {
                text.AppendLine($"  … ve {objects.Count - MaxListed} tane daha (tamamı konsolda)");
            }
        }

        private static string Path(GameObject go)
        {
            string path = go.name;
            Transform parent = go.transform.parent;
            while (parent != null)
            {
                path = parent.name + "/" + path;
                parent = parent.parent;
            }

            return path;
        }

        /// <summary>Açık olan TÜM sahnelerin kökleri (additive yüklü sahneler de denetlenir).</summary>
        private static IEnumerable<GameObject> EditorSceneRoots()
        {
            int count = UnityEngine.SceneManagement.SceneManager.sceneCount;
            for (int i = 0; i < count; i++)
            {
                UnityEngine.SceneManagement.Scene scene =
                    UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
                if (!scene.isLoaded)
                {
                    continue;
                }

                GameObject[] roots = scene.GetRootGameObjects();
                for (int r = 0; r < roots.Length; r++)
                {
                    yield return roots[r];
                }
            }
        }
    }
}
