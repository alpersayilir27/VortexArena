using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace VortexArena.Net.Editor
{
    /// <summary>
    /// sceneId bake yardımcıları: sahne içi <see cref="NetIdentity"/> toplama, boş id bulma,
    /// id yazma ve 0/çakışma onarımı. Hem <c>NetworkParentMenu</c> hem <c>SceneIdGuard</c>
    /// aynı mantığı buradan kullanır (tek doğruluk kaynağı).
    /// <para>
    /// Alan <c>private</c> olduğu için yazma HER ZAMAN <see cref="SerializedObject"/> üzerinden
    /// yapılır: runtime tarafında editor-only bir setter tutmayız (Net katmanı temiz kalır,
    /// prefab override ve Undo kaydı da kendiliğinden doğru işler).
    /// </para>
    /// </summary>
    internal static class SceneIdUtility
    {
        /// <summary>Serialize edilen alanın adı — <see cref="NetIdentity"/> içindekiyle birebir aynı olmalı.</summary>
        private const string SCENE_ID_PROPERTY = "sceneId";

        /// <summary>
        /// Sahnedeki tüm NetIdentity'leri toplar (PASİF objeler dahil). Sıra deterministiktir:
        /// kök obje sırası → <c>GetComponentsInChildren</c> hiyerarşi sırası. Onarımın iki
        /// çalıştırmada aynı sonucu vermesi bu sıraya dayanır.
        /// </summary>
        internal static List<NetIdentity> CollectInScene(Scene scene)
        {
            var result = new List<NetIdentity>();
            if (!scene.IsValid() || !scene.isLoaded)
            {
                return result;
            }

            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                result.AddRange(roots[i].GetComponentsInChildren<NetIdentity>(true));
            }

            return result;
        }

        /// <summary>Sahnedeki en büyük sceneId + 1 (sahne boşsa 1; 0 "atanmamış" için rezerve).</summary>
        internal static uint NextFreeId(Scene scene)
        {
            return NextFreeId(CollectInScene(scene));
        }

        /// <summary>
        /// Hazır toplanmış liste üzerinden max + 1. Döngü içinde sahneyi tekrar tekrar
        /// taramamak için liste alan aşırı yükleme.
        /// </summary>
        internal static uint NextFreeId(IReadOnlyList<NetIdentity> identities)
        {
            uint max = 0u;
            if (identities == null)
            {
                return 1u;
            }

            for (int i = 0; i < identities.Count; i++)
            {
                NetIdentity identity = identities[i];
                if (identity != null && identity.SceneId > max)
                {
                    max = identity.SceneId;
                }
            }

            return max + 1u;
        }

        /// <summary>
        /// sceneId'yi SerializedObject ile yazar ve objeyi dirty işaretler. Değer zaten
        /// istenen değerse hiçbir şey yapmaz (gereksiz sahne diff'i üretmemek için).
        /// Yazma gerçekten yapıldıysa true döner.
        /// </summary>
        internal static bool AssignId(NetIdentity identity, uint value)
        {
            if (identity == null || identity.SceneId == value)
            {
                return false;
            }

            var serialized = new SerializedObject(identity);
            SerializedProperty property = serialized.FindProperty(SCENE_ID_PROPERTY);
            if (property == null)
            {
                Debug.LogError(
                    $"[VortexArena] NetIdentity.{SCENE_ID_PROPERTY} alanı bulunamadı — alan yeniden mi adlandırıldı?",
                    identity);
                return false;
            }

            property.uintValue = value;
            serialized.ApplyModifiedProperties();
            EditorUtility.SetDirty(identity);
            return true;
        }

        /// <summary>
        /// Sahnedeki 0 kalan ve ÇAKIŞAN sceneId'leri onarır: bir id'yi İLK SAHİPLENEN korur,
        /// sonrakilere sıradaki boş id verilir. Tarama hiyerarşi sırasıyla yapıldığından sonuç
        /// deterministiktir. Onarım yapıldıysa true döner; <paramref name="fixedCount"/>
        /// değiştirilen bileşen sayısıdır.
        /// </summary>
        internal static bool RepairScene(Scene scene, out int fixedCount)
        {
            fixedCount = 0;

            List<NetIdentity> identities = CollectInScene(scene);
            if (identities.Count == 0)
            {
                return false;
            }

            var used = new HashSet<uint>();
            uint next = NextFreeId(identities);

            for (int i = 0; i < identities.Count; i++)
            {
                NetIdentity identity = identities[i];
                if (identity == null)
                {
                    continue;
                }

                // 0 = hiç atanmamış; used.Add false = bu id'yi önceki bir obje sahiplenmiş.
                if (identity.SceneId != 0u && used.Add(identity.SceneId))
                {
                    continue;
                }

                while (used.Contains(next))
                {
                    next++;
                }

                if (AssignId(identity, next))
                {
                    fixedCount++;
                }

                used.Add(next);
                next++;
            }

            return fixedCount > 0;
        }
    }
}
