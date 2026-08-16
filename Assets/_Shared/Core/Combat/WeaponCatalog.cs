using System;
using UnityEngine;

namespace VortexArena.Core.Combat
{
    /// <summary>
    /// Tüm silah tanımlarının kataloğu + uzak atış FX prefabı + ön kabza göstergesi prefabı.
    /// GameCatalog gibi Resources'ta yaşamak ZORUNDADIR
    /// (`Assets/_Shared/Data/Resources/WeaponCatalog.asset`): tüketiciler sahne/prefab
    /// referansı taşımadan <c>Resources.Load</c> ile okur. Admin/oyuncu ayrımı yoktur —
    /// iki rol de aynı kataloğu kullanır (admin gözlemcide de uzak atışlar oynatılır).
    /// Tüm sorgular null/boş girişe dayanıklıdır (eksik asset referansı akışı kırmasın).
    /// </summary>
    [CreateAssetMenu(fileName = "WeaponCatalog", menuName = "VortexArena/Weapon Catalog")]
    public class WeaponCatalog : ScriptableObject
    {
        /// <summary>Resources.Load anahtarı (asset dosya adıyla birebir).</summary>
        private const string ResourcePath = "WeaponCatalog";

        private static WeaponCatalog _cached;
        private static bool _loadAttempted;

        [SerializeField] private WeaponDefinition[] definitions = Array.Empty<WeaponDefinition>();
        [Tooltip("Uzak oyuncu atışlarının FX düğümü (RemoteShotFx havuzunda çoğaltılır); boşsa sade ses fallback'i üretilir.")]
        [SerializeField] private GameObject remoteShotFxPrefab;
        [Tooltip("Ön kabza SOKETİ — boş elin bileği ön kabzaya yaklaşınca Weapon bunu kavrama kaydına koyar ve " +
                 "kabul yarıçapının iki katına ölçekler (prefab 1 m ÇAP sözleşmesiyle tasarlanır; görülen küre = " +
                 "kabul hacmi). Tüm silahlar aynı sanatı paylaşır. Boşsa soket çizilmez, kavrama yine çalışır. " +
                 "Silah kiti koşusu (Configure All Build Elements) varsayılan küreyi üretip yalnız alan BOŞSA bağlar.")]
        [SerializeField] private GameObject secondaryGripIndicatorPrefab;

        /// <summary>Katalogdaki silah tanımları.</summary>
        public WeaponDefinition[] Definitions => definitions;

        /// <summary>Uzak atış FX prefabı (null olabilir).</summary>
        public GameObject RemoteShotFxPrefab => remoteShotFxPrefab;

        /// <summary>
        /// Ön kabza soketinin prefabı (null olabilir → soket çizilmez). Sanat buradadır;
        /// yerini/ölçeğini/alfasını <c>Weapon</c> sürer: ilk Renderer'ın materyaline
        /// (<c>_BaseColor</c>/<c>_Color</c>), Renderer yoksa <c>LineRenderer</c>'ın çizgi rengine yazılır.
        /// <para>⚠️ <b>1 m çap sözleşmesi:</b> prefab birim ölçüde tasarlanır, <c>Weapon</c> onu
        /// <c>2 × secondaryGripRadius</c>'a ölçekler — böylece çizilen küre kabul hacminin kendisidir.</para>
        /// </summary>
        public GameObject SecondaryGripIndicatorPrefab => secondaryGripIndicatorPrefab;

        /// <summary>weaponId ile tanım bulur (büyük/küçük harf duyarsız); yoksa/boşsa null.</summary>
        public WeaponDefinition FindByWeaponId(string id)
        {
            if (string.IsNullOrEmpty(id) || definitions == null)
            {
                return null;
            }

            for (int i = 0; i < definitions.Length; i++)
            {
                WeaponDefinition def = definitions[i];
                if (def != null && string.Equals(def.WeaponId, id, StringComparison.OrdinalIgnoreCase))
                {
                    return def;
                }
            }

            return null;
        }

        /// <summary>
        /// Kataloğu Resources'tan yükler; sonuç tek sefer önbelleklenir.
        /// Bulunamazsa TEK uyarı loglar ve null döner — çağıranlar null'a dayanıklı olmalı.
        /// </summary>
        public static WeaponCatalog Load()
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
            _cached = Resources.Load<WeaponCatalog>(ResourcePath);
            if (_cached == null)
            {
                Debug.LogWarning(
                    $"[WeaponCatalog] Resources'ta '{ResourcePath}' bulunamadı — silah tanımları ve uzak atış FX'i çalışmaz.");
            }

            return _cached;
        }
    }
}
