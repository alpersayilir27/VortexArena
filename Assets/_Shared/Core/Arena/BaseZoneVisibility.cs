using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

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
    /// ⚠️ <b>Silah kaynağıyla ilgisi YOKTUR.</b> Bu iş eskiden <c>WeaponGranter</c>'ın süpürmesine
    /// binmişti ve kapısı <c>weaponSource</c>'tu; FFA'da ikisi birlikte değiştiği için doğru
    /// görünüyordu. Lobinin silahı rastgeleye alınınca lobide de tabanlar kayboldu — kapı ayrıldı.
    /// </para>
    /// <para>
    /// ⚠️ <b>Yalnız KENDİ kapattığını geri açar.</b> Aynı bileşenleri <c>AdminSpectator</c> de
    /// kapatıyor (gözlemcinin ekranında taban takibi anlamsız); koşulsuz açan bir geri alma onun
    /// kararını sessizce bozardı.
    /// </para>
    /// <para>
    /// <b>Neden kendini önyükleyen tekil</b> (<c>WeaponGranter</c>/<c>PlayerCombatState</c>
    /// deseni): sahneye bileşen konsaydı her yeni arenaya elle bir kurulum adımı doğardı.
    /// </para>
    /// </summary>
    public class BaseZoneVisibility : MonoBehaviour
    {
        private static BaseZoneVisibility _instance;

        /// <summary>Bu bileşenin KAPATTIĞI bölgeler — başkasının kapattığı karışmasın diye ayrı
        /// tutulur. Sahne değişince referanslar ölür (Unity null'ı) ve liste yeniden kurulur.</summary>
        private readonly List<BaseZone> _disabledZones = new List<BaseZone>();

        /// <summary>Bu bileşenin GİZLEDİĞİ görsel şerit objeleri.</summary>
        private readonly List<GameObject> _hiddenObjects = new List<GameObject>();

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

            _instance = null;
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
            if (ShouldShow())
            {
                Restore();
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

                // ⚠️ Bölgenin GameObject'i KAPATILMAZ, bileşeni kapatılır: GameObject kapatılsaydı
                // altına konmuş marker'lar (ör. SpawnPoint) OnDisable'da statik kayıttan düşerdi.
                // Bileşeni kapatmak PlayerCombatState tarafından "açık taban yok" diye okunur.
                if (zone.enabled)
                {
                    zone.enabled = false;
                    _disabledZones.Add(zone);
                }

                HideStrip(zone);
            }
        }

        /// <summary>Taban bölgesinin görsel şeridi: Renderer'lı doğrudan çocuklar.
        /// <para>⚠️ Alt ağacında <see cref="SpawnPoint"/> BULUNAN çocuğa dokunulmaz — arenanın tek
        /// başlangıç noktası şeridin torunu olarak konmuş olabilir ve kapatılırsa <c>OnDisable</c>
        /// ile statik kayıttan düşer. Kontrol bu yüzden <c>GetComponent</c> değil
        /// <c>GetComponentInChildren</c>'dır.</para></summary>
        private void HideStrip(BaseZone zone)
        {
            Transform root = zone.transform;
            for (int i = 0; i < root.childCount; i++)
            {
                Transform child = root.GetChild(i);
                if (child.GetComponentInChildren<SpawnPoint>(true) != null ||
                    child.GetComponentInChildren<Renderer>(true) == null ||
                    !child.gameObject.activeSelf)
                {
                    continue;
                }

                child.gameObject.SetActive(false);
                _hiddenObjects.Add(child.gameObject);
            }
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
    }
}
