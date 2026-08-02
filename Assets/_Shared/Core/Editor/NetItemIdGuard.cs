using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using VortexArena.Core.Combat;

namespace VortexArena.Core.Editor
{
    /// <summary>
    /// <c>Tools &gt; VortexArena &gt; Weapons &gt; Rebuild Net Item Catalog</c> — tüm <see cref="ItemDefinition"/>
    /// asset'lerinin <c>netItemId</c>'lerinin atanmış ve TEKİL olduğunu doğrular (§6.6), sonra
    /// <c>Resources/NetItemCatalog.asset</c>'i projede BULUNAN eşyalardan yeniden yazar.
    /// <para>
    /// <b>Katalog neden silah tablosundan değil projeden türetiliyor:</b> <c>WeaponKitBuilder</c>
    /// yalnız tüfekleri bilir. Bomba (<c>ThrowableDefinition</c>) ya da başka bir eşya tipi
    /// eklendiğinde katalog o tabloya bağlı olsaydı sessizce eksik kalırdı — uzak oyuncularda
    /// bomba hiç çizilmezdi. <c>FindAssets("t:ItemDefinition")</c> tüm alt tipleri kapsar, yani
    /// yeni bir eşya TÜRÜ eklemek bu araca dokunmayı gerektirmez.
    /// </para>
    /// <para>
    /// ⚠️ Doğrulama DÜŞERSE katalog yazılmaz: çakışan kimliklerden kurulmuş bir katalog
    /// "çalışıyor gibi" görünüp yanlış eşya çizerdi — açık başarısızlık yeğdir.
    /// </para>
    /// <para>
    /// ⚠️ <b>Asıl korumayı taban sınıf değil bu bekçi sağlar.</b> Çakışan ya da atanmamış bir kimlik
    /// derlemede patlamaz, bir istisna atmaz, Inspector'da kırmızı görünmez — sahada "oyuncunun
    /// elinde yanlış eşya çizildi" (ya da hiç çizilmedi) olarak, hem de yalnız uzak istemcilerde
    /// görünür: atıcının kendi ekranında her şey doğrudur. Bu yüzden kimlik değişince ya da yeni
    /// eşya eklenince bu araç ELLE çalıştırılır.
    /// </para>
    /// <para>
    /// Kimlik telde giden bir bayt olduğu için katalog dizi indeksi kullanılmaz (dizi sırası
    /// değişince tüm eşyalar kayardı) — dolayısıyla tekilliği kimse otomatik garanti etmiyor.
    /// </para>
    /// </summary>
    internal static class NetItemIdGuard
    {
        private const string CatalogPath = "Assets/_Shared/Data/Resources/NetItemCatalog.asset";

        [MenuItem("Tools/VortexArena/Weapons/Rebuild Net Item Catalog", false, 23)]
        private static void Validate()
        {
            // Alt sınıflar (WeaponDefinition, ileride ThrowableDefinition) da t:ItemDefinition
            // süzgecine düşer — filtreyi türetilmiş tiplerle çoğaltmaya gerek yok.
            string[] guids = AssetDatabase.FindAssets("t:ItemDefinition");

            var byId = new Dictionary<byte, string>();
            var unassigned = new List<string>();
            var conflicts = new List<string>();
            int checkedCount = 0;

            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                ItemDefinition def = AssetDatabase.LoadAssetAtPath<ItemDefinition>(path);
                if (def == null)
                {
                    continue;
                }

                checkedCount++;

                if (!def.HasNetItemId)
                {
                    unassigned.Add(path);
                    continue;
                }

                if (byId.TryGetValue(def.NetItemId, out string other))
                {
                    conflicts.Add($"netItemId {def.NetItemId} → '{other}' ve '{path}'");
                    continue;
                }

                byId[def.NetItemId] = path;
            }

            var sb = new StringBuilder();
            sb.AppendLine($"{checkedCount} ItemDefinition tarandı.");

            if (unassigned.Count == 0 && conflicts.Count == 0)
            {
                sb.AppendLine("Tüm netItemId'ler atanmış ve tekil.");
                sb.Append(RebuildCatalog(byId));
                Debug.Log($"[NetItemIdGuard] {sb}");
                EditorUtility.DisplayDialog("VortexArena — Rebuild Net Item Catalog", sb.ToString(), "Tamam");
                return;
            }

            for (int i = 0; i < unassigned.Count; i++)
            {
                string msg = $"[NetItemIdGuard] netItemId ATANMAMIŞ (0 = boş el rezervi, geçersiz): " +
                             $"'{unassigned[i]}' — bu eşya uzak oyuncularda hiç çizilmez.";
                Debug.LogError(msg, AssetDatabase.LoadAssetAtPath<ItemDefinition>(unassigned[i]));
            }

            for (int i = 0; i < conflicts.Count; i++)
            {
                Debug.LogError($"[NetItemIdGuard] netItemId ÇAKIŞMASI: {conflicts[i]} — " +
                               "uzak oyuncularda yanlış eşya çizilir.");
            }

            if (unassigned.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine($"Atanmamış ({unassigned.Count}):");
                for (int i = 0; i < unassigned.Count; i++)
                {
                    sb.AppendLine($"  • {unassigned[i]}");
                }
            }

            if (conflicts.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine($"Çakışma ({conflicts.Count}):");
                for (int i = 0; i < conflicts.Count; i++)
                {
                    sb.AppendLine($"  • {conflicts[i]}");
                }
            }

            sb.AppendLine();
            sb.Append("⚠️ Katalog YAZILMADI — önce yukarıdakileri düzelt.");

            EditorUtility.DisplayDialog("VortexArena — Rebuild Net Item Catalog", sb.ToString(), "Tamam");
        }

        /// <summary>
        /// Doğrulanmış kimlik→yol eşlemesini <c>NetItemCatalog.asset</c>'e yazar. Sıralama
        /// <c>netItemId</c>'ye göredir: katalog diff'i kimlik ekleyip çıkarmaktan başka bir sebeple
        /// oynamasın (asset dosyası commit'lidir).
        /// </summary>
        private static string RebuildCatalog(Dictionary<byte, string> byId)
        {
            var catalog = AssetDatabase.LoadAssetAtPath<NetItemCatalog>(CatalogPath);
            if (catalog == null)
            {
                // Yolda başka tipte bir asset varsa üstüne YAZMA — CreateAsset GUID'i öldürür ve
                // ona referans veren her şey kopar (WeaponKitBuilder'daki aynı emniyet).
                if (!string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(CatalogPath)))
                {
                    return $"⚠️ '{CatalogPath}' yolunda NetItemCatalog olmayan bir asset var — dokunulmadı.";
                }

                catalog = ScriptableObject.CreateInstance<NetItemCatalog>();
                AssetDatabase.CreateAsset(catalog, CatalogPath);
            }

            var ids = new List<byte>(byId.Keys);
            ids.Sort();

            var items = new ItemDefinition[ids.Count];
            for (int i = 0; i < ids.Count; i++)
            {
                items[i] = AssetDatabase.LoadAssetAtPath<ItemDefinition>(byId[ids[i]]);
            }

            var so = new SerializedObject(catalog);
            SerializedProperty prop = so.FindProperty("items");
            if (prop == null || !prop.isArray)
            {
                return "⚠️ NetItemCatalog'ta 'items' dizisi yok (sözleşme kayması?) — katalog yazılmadı.";
            }

            prop.arraySize = items.Length;
            for (int i = 0; i < items.Length; i++)
            {
                prop.GetArrayElementAtIndex(i).objectReferenceValue = items[i];
            }

            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();

            return $"NetItemCatalog yazıldı: {items.Length} eşya (kimlikler {(ids.Count > 0 ? ids[0] : 0)}–{(ids.Count > 0 ? ids[ids.Count - 1] : 0)}).";
        }
    }
}
