using System;
using System.Collections.Generic;
using UnityEngine;

namespace VortexArena.Core.Combat
{
    /// <summary>
    /// Telde giden <c>netItemId</c> → <see cref="ItemDefinition"/> eşlemesi (§6.6).
    /// Uzak çizimin tek arama tablosudur: snapshot'tan gelen bayt burada bir prefaba ve kavrama
    /// pozuna çözülür.
    /// <para>
    /// <b>Resources'ta yaşamak ZORUNDADIR</b> (<c>Assets/_Shared/Data/Resources/NetItemCatalog.asset</c>):
    /// <c>RemoteAvatar</c> sahne/prefab referansı taşımaz, <c>Resources.Load</c> ile okur — asset
    /// <c>Resources/</c> altından çıkarılırsa uzak oyuncuların elinde hiçbir şey çizilmez (sessiz
    /// başarısızlık). <c>WeaponCatalog</c> ile aynı gerekçe.
    /// </para>
    /// <para>
    /// ⚠️ <b>Dizi sırası kimlik DEĞİLDİR.</b> Kimlik her tanımın kendi <c>netItemId</c> alanıdır;
    /// buradaki sıralama serbesttir ve değişmesi hiçbir şeyi kaydırmaz. Tekilliği
    /// <c>Configure All Build Elements</c> eşitlemesinde koşan net eşya kataloğu bekçisi korur.
    /// </para>
    /// Tüm sorgular null/boş girişe dayanıklıdır (eksik asset referansı akışı kırmasın).
    /// </summary>
    [CreateAssetMenu(fileName = "NetItemCatalog", menuName = "VortexArena/Net Item Catalog")]
    public class NetItemCatalog : ScriptableObject
    {
        /// <summary>Resources.Load anahtarı (asset dosya adıyla birebir).</summary>
        private const string ResourcePath = "NetItemCatalog";

        private static NetItemCatalog _cached;
        private static bool _loadAttempted;

        [Tooltip("Ağda kimliği olan tüm eşyalar (silah, bomba…). Sıralama serbesttir; kimlik " +
                 "her tanımın kendi netItemId alanıdır.")]
        [SerializeField] private ItemDefinition[] items = Array.Empty<ItemDefinition>();

        /// <summary>Katalogdaki eşya tanımları.</summary>
        public ItemDefinition[] Items => items;

        // Her karede aranıyor (uzak oyuncu × el başına bir sorgu), bu yüzden ilk çağrıda sözlük
        // kurulur. Katalog çalışma anında değişmez — asset yeniden yüklenirse ScriptableObject
        // örneği de yenidir, önbellek onunla birlikte gider.
        private Dictionary<byte, ItemDefinition> _byNetId;

        /// <summary>
        /// <c>netItemId</c> ile eşya tanımı bulur. <c>0</c> (el boş rezervi) ve bilinmeyen kimlik
        /// için null döner — çağıran "eşya çizme" olarak yorumlar.
        /// </summary>
        public ItemDefinition FindByNetItemId(byte netItemId)
        {
            if (netItemId == 0)
            {
                return null;
            }

            if (_byNetId == null)
            {
                BuildIndex();
            }

            return _byNetId.TryGetValue(netItemId, out ItemDefinition def) ? def : null;
        }

        private void BuildIndex()
        {
            _byNetId = new Dictionary<byte, ItemDefinition>();
            if (items == null)
            {
                return;
            }

            for (int i = 0; i < items.Length; i++)
            {
                ItemDefinition def = items[i];
                if (def == null || !def.HasNetItemId)
                {
                    continue;
                }

                // Çakışmada ilk giren kazanır ve burada SESSİZ kalınır: çalışma anında yapılacak
                // bir şey yok, doğru yer editör bekçisidir (NetItemIdGuard).
                _byNetId[def.NetItemId] = def;
            }
        }

        /// <summary>
        /// Kataloğu Resources'tan yükler; sonuç tek sefer önbelleklenir.
        /// Bulunamazsa TEK uyarı loglar ve null döner — çağıranlar null'a dayanıklı olmalı.
        /// </summary>
        public static NetItemCatalog Load()
        {
            if (_cached != null)
            {
                return _cached;
            }

            if (_loadAttempted)
            {
                return null;
            }

            _loadAttempted = true;
            _cached = Resources.Load<NetItemCatalog>(ResourcePath);
            if (_cached == null)
            {
                Debug.LogWarning(
                    $"[NetItemCatalog] Resources'ta '{ResourcePath}' bulunamadı — uzak oyuncuların " +
                    "elindeki eşyalar çizilmez.");
            }

            return _cached;
        }
    }
}
