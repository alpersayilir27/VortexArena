using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using VortexArena.Core.Combat;

namespace VortexArena.Core.Arena
{
    /// <summary>
    /// Taban bölgelerinin (<see cref="BaseZone"/>) görünür/etkin olup olmadığına karar veren
    /// <b>TEK</b> yer: kırmızı/mavi şeritler yalnız <b>takımlı</b> modda anlamlıdır.
    /// <list type="bullet">
    /// <item><b>Takımlı</b> (TDM, turnuva): şeritler durur — biri canlanma kapısı
    /// (<see cref="ModeReviveAnchor.OwnBase"/>), diğeri tur arası toplanma kapısıdır.</item>
    /// <item><b>Takımsız</b> (FFA): gizlenir — orada canlanma şartı sabit durmaktır, renkli bir
    /// şerit oyuncuya olmayan bir kural anlatırdı.</item>
    /// </list>
    /// <para>
    /// <b>Kapı hangi moddur:</b> öncelik <see cref="ModeSelection"/> (§5.3
    /// <c>selection_state</c>) — yani <b>seçili</b> mod. Sebep lobidir: admin bir arenayı
    /// sahnelediğinde herkes o arenaya geçer ama aktif kural hâlâ lobi profilidir (§10.7), yani
    /// koşan kurala bakan bir kapı "hangi maç kurulacak" sorusunu hiç göremezdi. Sunucu seçimi
    /// bildirmemişse (eski sunucu / bağlantı yok) <see cref="ModeRuntime"/>'ın takım kipine düşülür.
    /// </para>
    /// <para>
    /// <b>Duvar arkasından görünürlük (x-ray):</b> şerit görünür olduğunda, oyuncunun <b>KENDİ</b>
    /// takımının şeridine ikinci bir materyal slotu eklenir (<c>M_BaseZoneXRay</c>,
    /// <c>VortexArena/BaseZoneXRay</c> shader'ı). O materyal <c>ZTest Greater</c> ile çizildiği için
    /// yalnız şeridin ÖNÜNDE başka geometri olan piksellerde görünür: arena dekorla dolsa bile ölen
    /// oyuncu canlanmak için nereye yürüyeceğini görür. Aynı mesh'in ikinci çizimi olduğu için ne
    /// yeni GameObject ne URP renderer feature ne de yeni katman gerekir.
    /// <list type="bullet">
    /// <item><b>Rakip taban asla çizilmez</b> — slot hiç eklenmez.</item>
    /// <item>Takım <see cref="Team.Neutral"/> ise (takım atanmadı, admin gözlemci) hiç eklenmez.</item>
    /// <item>Takım rengi <b>şeridin kendi materyalinden</b> okunur — ikinci bir renk tanımı doğmasın.</item>
    /// </list>
    /// </para>
    /// <para>
    /// ⚠️ <b>Silah kaynağıyla ilgisi YOKTUR.</b> Bu iş eskiden <c>WeaponGranter</c>'ın süpürmesine
    /// binmişti ve kapısı <c>weaponSource</c>'tu; FFA'da ikisi birlikte değiştiği için doğru
    /// görünüyordu. Lobinin silahı rastgeleye alınınca lobide de tabanlar kayboldu — kapı ayrıldı.
    /// </para>
    /// <para>
    /// ⚠️ <b>Yalnız KENDİ kapattığını geri açar.</b> Aynı bileşenleri <c>AdminSpectator</c> de
    /// kapatıyor (gözlemcinin ekranında taban takibi anlamsız); koşulsuz açan bir geri alma onun
    /// kararını sessizce bozardı. Aynı sebeple x-ray de yalnız KENDİ eklediği slotları söker.
    /// </para>
    /// <para>
    /// <b>Neden kendini önyükleyen tekil</b> (<c>WeaponGranter</c>/<c>PlayerCombatState</c>
    /// deseni): sahneye bileşen konsaydı her yeni arenaya elle bir kurulum adımı doğardı.
    /// </para>
    /// </summary>
    public class BaseZoneVisibility : MonoBehaviour
    {
        /// <summary>X-ray materyalinin <c>Resources</c> yolu. ⚠️ Materyal <c>Resources/</c> altında
        /// durmalı: hiçbir sahneden referans verilmediği için shader aksi hâlde build'den strip
        /// edilir ve Quest'te şerit pembe çizilir.</summary>
        private const string XRayMaterialResource = "M_BaseZoneXRay";

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private static BaseZoneVisibility _instance;

        /// <summary>Bu bileşenin KAPATTIĞI bölgeler — başkasının kapattığı karışmasın diye ayrı
        /// tutulur. Sahne değişince referanslar ölür (Unity null'ı) ve liste yeniden kurulur.</summary>
        private readonly List<BaseZone> _disabledZones = new List<BaseZone>();

        /// <summary>Bu bileşenin GİZLEDİĞİ görsel şerit objeleri.</summary>
        private readonly List<GameObject> _hiddenObjects = new List<GameObject>();

        /// <summary>X-ray slotu EKLENEN renderer'lar ve onlara verilen materyal örnekleri.
        /// İkisi de yalnız bu bileşene aittir; sökerken kimin ne koyduğu buradan bilinir.</summary>
        private readonly List<Renderer> _xrayRenderers = new List<Renderer>();

        private readonly List<Material> _xrayMaterials = new List<Material>();

        /// <summary>Materyal dizisi okuma/yazma kuyruğu — kare başına çöp üretmemek için.</summary>
        private readonly List<Material> _materialScratch = new List<Material>();

        private Material _xrayShared;
        private bool _xrayLoadFailed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (_instance != null)
            {
                return;
            }

            var go = new GameObject("[BaseZoneVisibility]");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<BaseZoneVisibility>();
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;

            // Kalıcı tekiliz: obje devre dışı bırakılsa bile olaylar kaçmasın diye Awake/OnDestroy
            // (PlayerCombatState deseni).
            SceneManager.sceneLoaded += HandleSceneLoaded;
            ModeSelection.Changed += Apply;
            ModeRuntime.Changed += Apply;
            PlayerCombatState.LocalTeamChanged += HandleLocalTeamChanged;
            Apply();
        }

        private void OnDestroy()
        {
            if (_instance != this)
            {
                return;
            }

            SceneManager.sceneLoaded -= HandleSceneLoaded;
            ModeSelection.Changed -= Apply;
            ModeRuntime.Changed -= Apply;
            PlayerCombatState.LocalTeamChanged -= HandleLocalTeamChanged;

            ClearXRay();

            _instance = null;
        }

        private void HandleLocalTeamChanged(Team team)
        {
            // Takım değişimi yalnız x-ray'i ilgilendiriyor ama Apply zaten idempotent: ayrı bir
            // dar yol açmak ikinci bir uygulama noktası olurdu.
            Apply();
        }

        private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            // Yeni sahne = yeni bölgeler; eskiler sahneyle birlikte gitti. Listeler ölü
            // referanslarla dolmasın diye BOŞALTILIR — geri alma da anlamını yitirdi.
            _disabledZones.Clear();
            _hiddenObjects.Clear();
            Apply();
        }

        /// <summary>Kararın tek uygulama noktası. Bölgeler sahnede aranır: bu bileşen sahne
        /// yüklenmeden de doğabilir ve <c>ModeSelection</c> sahneden bağımsız değişir.</summary>
        private void Apply()
        {
            // Her koşulda önce sökülür: mod da takım da değişmiş olabilir ve slot yığılmamalı.
            ClearXRay();

            if (ShouldShow())
            {
                Restore();
                ApplyXRay();
                return;
            }

            Hide();
        }

        /// <summary>Seçili mod varsa o, yoksa koşan kural. "Bilinmiyor" ile "takımsız" ayrı
        /// durumlardır — eski sunucuda bugünkü davranış korunsun diye kural devralır.</summary>
        private static bool ShouldShow()
        {
            return ModeSelection.HasValue ? !ModeSelection.IsTeamless : !ModeRuntime.IsTeamless;
        }

        private void Hide()
        {
            BaseZone[] zones = FindObjectsByType<BaseZone>(FindObjectsSortMode.None);
            for (int i = 0; i < zones.Length; i++)
            {
                BaseZone zone = zones[i];
                if (zone == null)
                {
                    continue;
                }

                // ⚠️ Bölgenin GameObject'i KAPATILMAZ, bileşeni kapatılır: bileşeni kapatmak
                // PlayerCombatState tarafından "açık taban yok" diye okunur, GameObject'i kapatmak
                // ise altındaki HER ŞEYİ (görsel şerit dahil) kapatır ve Restore'da neyi geri
                // açacağımızı bulanıklaştırır — şeridi ayrıca HideStrip yönetiyor.
                if (zone.enabled)
                {
                    zone.enabled = false;
                    _disabledZones.Add(zone);
                }

                HideStrip(zone);
            }
        }

        /// <summary>Taban bölgesinin görsel şeridi: Renderer'lı doğrudan çocuklar.</summary>
        private void HideStrip(BaseZone zone)
        {
            Transform root = zone.transform;
            for (int i = 0; i < root.childCount; i++)
            {
                Transform child = root.GetChild(i);
                if (!IsStripChild(child) || !child.gameObject.activeSelf)
                {
                    continue;
                }

                child.gameObject.SetActive(false);
                _hiddenObjects.Add(child.gameObject);
            }
        }

        /// <summary>Bu doğrudan çocuk "görsel şerit" mi. <see cref="HideStrip"/> ile x-ray aynı
        /// kümeye bakmalı — ayrı iki seçim kuralı sessizce sapardı.</summary>
        private static bool IsStripChild(Transform child)
        {
            return child.GetComponentInChildren<Renderer>(true) != null;
        }

        /// <summary>Yalnız bu bileşenin kapattıklarını geri açar; ölü referanslar atlanır.</summary>
        private void Restore()
        {
            for (int i = 0; i < _hiddenObjects.Count; i++)
            {
                if (_hiddenObjects[i] != null)
                {
                    _hiddenObjects[i].SetActive(true);
                }
            }

            for (int i = 0; i < _disabledZones.Count; i++)
            {
                if (_disabledZones[i] != null)
                {
                    _disabledZones[i].enabled = true;
                }
            }

            _hiddenObjects.Clear();
            _disabledZones.Clear();
        }

        // ------------------------------------------------------------------------- x-ray

        /// <summary>Yerel oyuncunun takımına ait şeritlere duvar-arkası çizim slotunu ekler.</summary>
        private void ApplyXRay()
        {
            Team local = ArenaCombat.LocalTeam;
            if (local == Team.Neutral)
            {
                // Takım yok (henüz atanmadı / admin gözlemci): kimin şeridi olduğu belli değil.
                return;
            }

            Material shared = ResolveXRayMaterial();
            if (shared == null)
            {
                return;
            }

            BaseZone[] zones = FindObjectsByType<BaseZone>(FindObjectsSortMode.None);
            for (int i = 0; i < zones.Length; i++)
            {
                BaseZone zone = zones[i];
                if (zone == null || zone.Team != local)
                {
                    continue;
                }

                Transform root = zone.transform;
                for (int c = 0; c < root.childCount; c++)
                {
                    Transform child = root.GetChild(c);
                    if (!IsStripChild(child))
                    {
                        continue;
                    }

                    Renderer[] renderers = child.GetComponentsInChildren<Renderer>(true);
                    for (int r = 0; r < renderers.Length; r++)
                    {
                        AddXRaySlot(renderers[r], shared);
                    }
                }
            }
        }

        /// <summary>
        /// Renderer'a ikinci bir materyal ekler: aynı mesh bir kez daha, ters derinlik testiyle.
        /// <para>
        /// ⚠️ <c>renderer.materials</c> <b>getter'ı kullanılmaz</b> — mevcut takım materyalini de
        /// klonlar ve paylaşılan materyalle bağını koparırdı.
        /// </para>
        /// </summary>
        private void AddXRaySlot(Renderer renderer, Material shared)
        {
            if (renderer == null)
            {
                return;
            }

            renderer.GetSharedMaterials(_materialScratch);

            // Elle konmuş ya da artakalmış bir slot varsa ikincisini ekleme (idempotent).
            for (int i = 0; i < _materialScratch.Count; i++)
            {
                Material existing = _materialScratch[i];
                if (existing != null && existing.shader == shared.shader)
                {
                    return;
                }
            }

            var ghost = new Material(shared) { name = shared.name + " (runtime)" };
            CopyTeamColor(_materialScratch.Count > 0 ? _materialScratch[0] : null, ghost);

            _materialScratch.Add(ghost);
            renderer.sharedMaterials = _materialScratch.ToArray();

            _xrayRenderers.Add(renderer);
            _xrayMaterials.Add(ghost);
        }

        /// <summary>Takım rengi TEK kaynakta kalsın diye şeridin kendi materyalinden okunur
        /// (<c>M_TeamRed</c>/<c>M_TeamBlue</c>); x-ray materyali renk taşımaz.</summary>
        private static void CopyTeamColor(Material source, Material ghost)
        {
            if (source == null)
            {
                return;
            }

            if (source.HasProperty(BaseColorId))
            {
                ghost.SetColor(BaseColorId, source.GetColor(BaseColorId));
            }
            else if (source.HasProperty(ColorId))
            {
                ghost.SetColor(BaseColorId, source.GetColor(ColorId));
            }
        }

        /// <summary>Yalnız bu bileşenin eklediği slotları söker ve ürettiği materyalleri yok eder.
        /// Ölü renderer'lar (sahne değişti) atlanır — materyaller yine de temizlenir.</summary>
        private void ClearXRay()
        {
            for (int i = 0; i < _xrayRenderers.Count; i++)
            {
                Renderer renderer = _xrayRenderers[i];
                if (renderer == null)
                {
                    continue;
                }

                renderer.GetSharedMaterials(_materialScratch);
                int removed = _materialScratch.RemoveAll(IsOwnXRayMaterial);
                if (removed > 0)
                {
                    renderer.sharedMaterials = _materialScratch.ToArray();
                }
            }

            for (int i = 0; i < _xrayMaterials.Count; i++)
            {
                if (_xrayMaterials[i] != null)
                {
                    Destroy(_xrayMaterials[i]);
                }
            }

            _xrayRenderers.Clear();
            _xrayMaterials.Clear();
        }

        private bool IsOwnXRayMaterial(Material material)
        {
            return material != null && _xrayMaterials.Contains(material);
        }

        /// <summary>Paylaşılan x-ray materyali; bulunamazsa <b>bir kez</b> hata basar ve bir daha
        /// denenmez (her sahne yüklemesinde aynı hatayı tekrarlamasın).</summary>
        private Material ResolveXRayMaterial()
        {
            if (_xrayShared != null)
            {
                return _xrayShared;
            }

            if (_xrayLoadFailed)
            {
                return null;
            }

            _xrayShared = Resources.Load<Material>(XRayMaterialResource);
            if (_xrayShared == null)
            {
                _xrayLoadFailed = true;
                Debug.LogError($"BaseZoneVisibility: '{XRayMaterialResource}' Resources altında " +
                               "bulunamadı — taban şeridi duvar arkasından görünmeyecek.");
            }

            return _xrayShared;
        }
    }
}
