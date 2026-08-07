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
    /// <c>ObstacleVolumes</c> böyle bir collider'ı eleyip hata basıyor, ama o satır ancak biri
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
            var swollen = new List<GameObject>();
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

                    if (IsSwollen(go, collider))
                    {
                        swollen.Add(go);
                        continue;
                    }

                    healthy++;
                }
            }

            Report(nonConvex, triggers, colliderless, swollen, healthy);
        }

        // ---------------------------------------------------------------- şişkinlik

        /// <summary>Şişkinlik testinde en fazla kaç üçgen örneklenir (büyük mesh'te eşit aralıkla).</summary>
        private const int MaxSwellSamples = 200;

        /// <summary>Yüzeyin iki yanına taşıma mesafesi (m). Collider yüzeyle çakışıyorsa bir yan
        /// içeride, öteki dışarıda olur; ikisi de içerideyse collider oradan şişkindir.</summary>
        private const float SwellProbeMeters = 0.02f;

        /// <summary>Şişkin saymak için gereken en az örnek oranı ve sayısı (tekil sayısal gürültü
        /// rapor üretmesin).</summary>
        private const float SwellRatioThreshold = 0.02f;
        private const int SwellMinHits = 3;

        /// <summary>
        /// Collider, <b>kendi kaynak mesh'inin yüzeyinden</b> şişkin mi. Kalan geometrik tuzak
        /// budur: konveks işaretlenmiş <b>içbükey</b> bir mesh'te hull çukuru doldurur, collider
        /// görünenden büyür ve oyuncu <b>boşlukta</b> ceza alır — gözle denetlenemez, çünkü çizilen
        /// mesh doğrudur.
        /// <para>Ölçüt çalışma anındaki testin <b>aynısıdır</b> (<c>ClosestPoint</c> ile
        /// nokta-içeride, <c>ObstacleVolumes</c>): üçgen ağırlık merkezleri normal boyunca dışarı
        /// taşınır ve "hâlâ içeride mi" diye sorulur. İkinci bir doğruluk kaynağı doğmasın diye
        /// hacim/hull karşılaştırması gibi ayrı bir matematik kullanılmaz.</para>
        /// <para>⚠️ <b>Yalnız <see cref="MeshCollider"/> denetlenir</b> ve kaynak, MeshFilter değil
        /// <b>collider'ın kendi mesh'idir</b>. Sebep: yalnız MeshCollider "ben bu yüzeyim" iddiası
        /// taşır. Box/Capsule zaten bilinçli bir <b>kabalaştırmadır</b> (dokümanın önerdiği çözüm
        /// yolu) — onu şişkin diye raporlamak, aracın kendi tavsiyesini hata saymasıdır.</para>
        /// <para>Mesh okunamıyorsa (Read/Write kapalı import) ya da collider/obje kapalıysa test
        /// yapılmaz — <c>false</c> döner: denetlenememiş bir obje "sorunlu" diye raporlanmaz.
        /// ⚠️ Kapalı collider'da <c>ClosestPoint</c> girdi noktasını aynen döndürür, yani bu kapı
        /// olmadan her kapalı obje "şişkin" okunurdu.</para>
        /// </summary>
        private static bool IsSwollen(GameObject go, Collider collider)
        {
            if (collider is not MeshCollider meshCollider || !collider.enabled ||
                !go.activeInHierarchy)
            {
                return false;
            }

            Mesh mesh = meshCollider.sharedMesh;
            if (mesh == null || !mesh.isReadable)
            {
                return false;
            }

            int[] triangles = mesh.triangles;
            Vector3[] vertices = mesh.vertices;
            int triangleCount = triangles.Length / 3;
            if (triangleCount == 0)
            {
                return false;
            }

            Transform tr = go.transform;
            int step = Mathf.Max(1, triangleCount / MaxSwellSamples);
            int sampled = 0;
            int inside = 0;

            for (int t = 0; t < triangleCount; t += step)
            {
                int i = t * 3;
                Vector3 a = vertices[triangles[i]];
                Vector3 b = vertices[triangles[i + 1]];
                Vector3 c = vertices[triangles[i + 2]];

                Vector3 normal = Vector3.Cross(b - a, c - a);
                if (normal.sqrMagnitude <= 1e-12f)
                {
                    continue; // dejenere üçgen
                }

                Vector3 worldCentroid = tr.TransformPoint((a + b + c) / 3f);
                Vector3 offset = tr.TransformDirection(normal).normalized * SwellProbeMeters;

                // ⚠️ Yüzeyin İKİ yanı da sınanır ve ölçüt "ikisi de içeride"dir. Tek yana bakmak
                // üçgen sarım yönüne (winding) güvenmek olurdu: yön ters okunursa tüm noktalar
                // gövdenin içine düşer ve araç HER objeyi şişkin raporlardı. İki yanı da içeride
                // olan bir yüzey ise sarımdan bağımsız olarak collider'ın İÇİNE gömülmüştür —
                // aranan durum tam olarak budur (hull çukuru doldurmuş).
                sampled++;
                if (IsInside(collider, worldCentroid + offset) &&
                    IsInside(collider, worldCentroid - offset))
                {
                    inside++;
                }
            }

            return sampled > 0 && inside >= SwellMinHits && inside >= sampled * SwellRatioThreshold;
        }

        /// <summary>Konveks collider'da içerideki bir nokta için <c>ClosestPoint</c> noktanın
        /// KENDİSİDİR (çalışma anındaki <c>ObstacleVolumes</c> testinin aynısı).</summary>
        private static bool IsInside(Collider collider, Vector3 point) =>
            (collider.ClosestPoint(point) - point).sqrMagnitude <= 1e-8f;

        private static void Report(List<GameObject> nonConvex, List<GameObject> triggers,
            List<GameObject> colliderless, List<GameObject> swollen, int healthy)
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

            if (swollen.Count > 0)
            {
                text.AppendLine();
                text.AppendLine($"⚠️ ŞİŞKİN ({swollen.Count}) — collider görünen yüzeyin DIŞINA taşıyor.");
                text.AppendLine("Sebebi genelde içbükey bir mesh'in convex işaretlenmesidir (hull");
                text.AppendLine("çukuru doldurur). Oyuncu bu objelerde BOŞLUKTA ceza alır — şekli");
                text.AppendLine("konveks parçalara böl ya da kaba bir Box/Capsule kullan:");
                AppendNames(text, swollen);
            }

            if (colliderless.Count > 0)
            {
                text.AppendLine();
                text.AppendLine($"ℹ️ Collider'sız ({colliderless.Count}) — layer damgalı ama collider yok;");
                text.AppendLine("bu objeler tespitte hiç görünmez:");
                AppendNames(text, colliderless);
            }

            if (nonConvex.Count == 0 && triggers.Count == 0 && colliderless.Count == 0 &&
                swollen.Count == 0)
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

            for (int i = 0; i < swollen.Count; i++)
            {
                Debug.LogWarning($"[Engel denetimi] '{Path(swollen[i])}' collider'ı görünen yüzeyden " +
                                 "şişkin — oyuncu bu objede boşlukta ceza alır.", swollen[i]);
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
