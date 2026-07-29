using System.Collections.Generic;
using UnityEngine;

namespace VortexArena.Core.Combat
{
    /// <summary>
    /// Rafın silah kaynağı: kural <see cref="ModeWeaponSource.Rack"/> iken gözlere
    /// (<see cref="RackSlot"/>) modun loadout'undan silah örnekler, kural değişince toplar.
    /// <para>
    /// <b>Neden sahneye elle silah konmuyor:</b> elle konan her <c>WPN_*</c> örneği sahneye
    /// donmuş bir kopya olarak yazılır — moda silah eklendiğinde ya da denge değiştiğinde her
    /// arenayı tek tek açmak gerekirdi. Göz yalnız KONUMU (sanat kararı) tutar; hangi silahın
    /// duracağı <c>ModeDefinition.loadout</c>'tan (mod kararı) gelir.
    /// </para>
    /// <para>
    /// Silahlar prefabındaki fizikle örneklenir (kütleli, yer çekimli) — mevcut davranış budur,
    /// raf mesh'inin çarpıştırıcısı üstünde dururlar. Kavrama/fizik ayarlarına DOKUNULMAZ:
    /// dokunulsaydı raftan alınan silah <see cref="WeaponGranter"/>'ın verdiği silah gibi
    /// davranırdı (elde sabit, bırakılamaz).
    /// </para>
    /// <para>
    /// <b>Kurulum:</b> raf köküne konur, <see cref="slots"/> listesine raf üstündeki boş
    /// Transform'lar sürüklenir. Gözün <see cref="RackSlot.weapon"/> alanı boşsa loadout'tan
    /// sırayla doldurulur.
    /// </para>
    /// </summary>
    public class WeaponRackSpawner : MonoBehaviour
    {
        [Tooltip("Raf gözleri — her göze bir silah gelir. Sıra, loadout sırasına karşılık gelir.")]
        [SerializeField] private List<RackSlot> slots = new List<RackSlot>();

        /// <summary>Bu rafın ürettiği örnekler — kural değişince toplanabilsin diye tutulur.</summary>
        private readonly List<GameObject> _spawned = new List<GameObject>();

        /// <summary>Rastgele değil SIRAYLA dağıtımın tamponu (loadout'un prefablı olanları).</summary>
        private readonly List<WeaponDefinition> _pool = new List<WeaponDefinition>();

        private bool _loadoutWarned;

        private void OnEnable()
        {
            ModeRuntime.Changed += Apply;
            Apply();
        }

        private void OnDisable()
        {
            ModeRuntime.Changed -= Apply;
        }

        // ------------------------------------------------------------------ kural

        /// <summary>Kuralın tek uygulama noktası: Rack'te doldur, değilse topla.</summary>
        private void Apply()
        {
            if (ModeRuntime.Weapons == ModeWeaponSource.Rack)
            {
                Fill();
                return;
            }

            Clear();
        }

        // --------------------------------------------------------------- doldurma

        private void Fill()
        {
            if (_spawned.Count > 0)
            {
                return; // zaten dolu — maç içi kural tekrarında rafı yeniden kurmayız
            }

            BuildPool();

            for (int i = 0; i < slots.Count; i++)
            {
                RackSlot slot = slots[i];
                if (slot == null || slot.anchor == null)
                {
                    continue;
                }

                WeaponDefinition definition = ResolveDefinition(slot, i);
                if (definition == null || definition.Prefab == null)
                {
                    WarnMissingLoadout(definition);
                    continue;
                }

                GameObject instance = Instantiate(
                    definition.Prefab, slot.anchor.position, slot.anchor.rotation, transform);
                instance.name = definition.Prefab.name;
                _spawned.Add(instance);
            }
        }

        /// <summary>Gözün kendi silahı varsa o; yoksa loadout'tan göz sırasına düşen.</summary>
        private WeaponDefinition ResolveDefinition(RackSlot slot, int index)
        {
            if (slot.weapon != null)
            {
                return slot.weapon;
            }

            return _pool.Count == 0 ? null : _pool[index % _pool.Count];
        }

        /// <summary>Loadout'u <see cref="WeaponGranter"/> ile AYNI yoldan okur (katalog →
        /// mod → loadout); prefabı olmayan tanımlar havuza girmez.</summary>
        private void BuildPool()
        {
            _pool.Clear();

            GameCatalog catalog = Resources.Load<GameCatalog>("GameCatalog");
            ModeDefinition mode = catalog != null ? catalog.FindMode(ModeRuntime.ModeId) : null;
            WeaponDefinition[] loadout = mode != null ? mode.Loadout : null;
            if (loadout == null)
            {
                return;
            }

            for (int i = 0; i < loadout.Length; i++)
            {
                if (loadout[i] != null && loadout[i].Prefab != null)
                {
                    _pool.Add(loadout[i]);
                }
            }
        }

        // ---------------------------------------------------------------- toplama

        private void Clear()
        {
            for (int i = 0; i < _spawned.Count; i++)
            {
                if (_spawned[i] != null)
                {
                    Destroy(_spawned[i]);
                }
            }

            _spawned.Clear();
        }

        private void WarnMissingLoadout(WeaponDefinition definition)
        {
            if (_loadoutWarned)
            {
                return;
            }

            _loadoutWarned = true;
            Debug.LogWarning(definition == null
                ? $"[WeaponRackSpawner] '{ModeRuntime.ModeId}' modunun loadout'u boş (ya da katalogda " +
                  "yok); raf doldurulamıyor — ModeDefinition.loadout'a prefablı WeaponDefinition ekle."
                : $"[WeaponRackSpawner] '{definition.name}' tanımının prefabı yok; göz boş kaldı.", this);
        }
    }
}
