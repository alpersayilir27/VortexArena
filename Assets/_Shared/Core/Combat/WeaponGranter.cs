using System.Collections.Generic;
using Oculus.Interaction;
using Oculus.Interaction.Input;
using UnityEngine;
using UnityEngine.SceneManagement;
using VortexArena.Core.Arena;
using VortexArena.Net;
using VortexArena.Protocol;

namespace VortexArena.Core.Combat
{
    /// <summary>
    /// Oyuncunun eline silah koyan <b>TEK</b> yer — iki ayrı kaynaktan beslenir ve hangisinin
    /// işlediğini mod kuralı (<see cref="ModeWeaponSource"/>) belirler:
    /// <list type="number">
    /// <item><b>RandomGrant</b> (§10.5 <c>weaponSource:"random"</c>): sahnedeki rafı/taban
    /// bölgelerini SÜPÜRÜR ve grip'e basılı tutulan her elde loadout'tan rastgele bir silah durur;
    /// bırakılınca <b>yok olur</b>, tekrar basınca YENİSİ gelir (<see cref="WeaponGrantKind.Disposable"/>).</item>
    /// <item><b>Çerçeve</b> (<see cref="WeaponFrame"/>, <see cref="ModeWeaponSource.Rack"/>): silah
    /// sahnede çerçevesinde sabit durur, oyuncu onu ≤2 m'den uzaktan SEÇER
    /// (<see cref="SelectWeapon"/>); grip'e basınca seçilen silahın KLONU eline gelir, bırakınca
    /// <b>yalnız gizlenir</b> — aynı örnek aynı mermiyle geri gelir
    /// (<see cref="WeaponGrantKind.Persistent"/>). Oyuncu başına TEK silah.</item>
    /// </list>
    /// <para>
    /// <b>Neden ikisi de burada:</b> bu sınıf zaten rig/el anchor'ını çözen, grip'i yoklayan, ölünce
    /// silahı geri alan ve sahne değişimini temizleyen yerdir. İkinci bir tekil açılsaydı ikinci bir
    /// rig keşif yolu doğardı — iki arama farklı karelerde farklı rig bulur ve silah el değiştirmiş
    /// gibi zıplardı (bkz. <see cref="ResolveHandAnchor"/> notu).
    /// </para>
    /// <para>
    /// <b>Neden kendini önyükleyen tekil</b> (<see cref="PlayerCombatState"/> deseni): sahneye
    /// bileşen konsaydı her yeni arenaya elle bir kurulum adımı doğardı ve CLAUDE.md'deki arena
    /// listesi büyürdü. Bu bileşen görünmezdir.
    /// </para>
    /// <para>
    /// <b>Admin gözlemcide iki yol da kendiliğinden kapalıdır:</b> <c>AdminSpectator</c> BB rig'i
    /// kapattığı için <see cref="OVRCameraRig"/> aranınca bulunamaz (arama pasif objeleri dahil
    /// etmez) → el anchor'ı yok → silah verilmez. Süpürme ise role bakmaz: raf FFA'da gözlemcinin
    /// ekranında da durmamalı.
    /// </para>
    /// </summary>
    public class WeaponGranter : MonoBehaviour
    {
        /// <summary>Grip eşiği — <c>PrimaryHandTrigger</c> analog okunur; yarım basış "tutuyor"
        /// sayılmaz ki silah titremesin.</summary>
        private const float GripThreshold = 0.55f;

        /// <summary>El anchor'ı bulunamadığında yeniden arama aralığı (sahne yeni yüklenmiş olabilir).</summary>
        private const float RigRescanSeconds = 1f;

        public static WeaponGranter Instance { get; private set; }

#if UNITY_EDITOR
        /// <summary>
        /// <b>YALNIZ editör (dev sandbox):</b> loadout'tan rastgele değil <b>SIRAYLA</b> dağıt —
        /// grip'e her basışta bir sonraki silah. Bütün silahları tek tek gözden geçirmek
        /// (duruş, namlu, ses) rastgelelikte bir tesadüf oyununa dönüşüyordu.
        /// <para>
        /// ⚠️ <b>Üretim davranışı DEĞİŞMEZ</b>: bu bayrağı yalnız <c>DevSession</c> yazar, build'e
        /// hiç girmez ve mod kuralı (<see cref="ModeWeaponSource.RandomGrant"/>) rastgele kalır —
        /// rastgelelik FFA'da bir oyun tasarımı kararıdır, test kolaylığı için bozulmaz.
        /// </para>
        /// </summary>
        public static bool SequentialGrant { get; set; }

        /// <summary>Sıralı dağıtımın imleci; ilk grip'te loadout'un İLK silahı gelsin diye -1.</summary>
        private int _sequentialIndex = -1;
#endif

        /// <summary>Sol/sağ elde duran TEK KULLANIMLIK silah örneği (yoksa null).</summary>
        private Weapon _grantedLeft;
        private Weapon _grantedRight;

        /// <summary>Çerçeveden seçilen silah — oyuncu başına TEK. Harita başına sıfırlanır
        /// (bkz. <see cref="HandleSceneLoaded"/>).</summary>
        private WeaponDefinition _selected;

        /// <summary>Seçili silahın KALICI klonu: bırakılınca yok edilmez, yalnız gizlenir —
        /// "aynı silah aynı mermiyle geri gelir" kuralı bu tek örnekten doğar.</summary>
        private Weapon _summoned;

        /// <summary>Klonun şu an hangi elde olduğu (<c>None</c> = gizli).</summary>
        private OVRInput.Controller _summonedHand = OVRInput.Controller.None;

        private OVRCameraRig _rig;
        private float _nextRigScanAt;
        private bool _aliveSubscribed;

        /// <summary>Süpürmenin GİZLEDİĞİ objeler — kural değişince geri açılabilsin diye tutulur.
        /// Sahne değişince referanslar ölür (Unity null'ı) ve liste yeniden kurulur.</summary>
        private readonly List<GameObject> _hiddenObjects = new List<GameObject>();

        /// <summary>Süpürmenin KAPATTIĞI taban bileşenleri (GameObject'leri kapatılmaz — bkz.
        /// <see cref="SweepScene"/>).</summary>
        private readonly List<BaseZone> _disabledZones = new List<BaseZone>();

        /// <summary>Rastgele seçimin havuzu — her karede yeni liste ayırmamak için tampon.</summary>
        private readonly List<WeaponDefinition> _pool = new List<WeaponDefinition>();

        private bool _swept;
        private bool _loadoutWarned;
        private bool _summonPrefabWarned;

        /// <summary>Son uygulanan silah kaynağı — yalnız GEÇİŞTE çerçeve durumunu sıfırlamak için
        /// (bkz. <see cref="ApplyRules"/>).</summary>
        private bool _appliedRandomGrant;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (Instance != null)
            {
                return;
            }

            var go = new GameObject("[WeaponGranter]");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<WeaponGranter>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;

            // Kalıcı tekiliz: obje devre dışı bırakılsa bile kural/sahne olayları kaçmasın diye
            // Awake/OnDestroy'da abone oluruz (PlayerCombatState deseni).
            SceneManager.sceneLoaded += HandleSceneLoaded;
            ModeRuntime.Changed += HandleRulesChanged;
            NetEvents.OnCountdown += HandleCountdown;
            ApplyRules();
        }

        private void OnDestroy()
        {
            if (Instance != this)
            {
                return;
            }

            SceneManager.sceneLoaded -= HandleSceneLoaded;
            ModeRuntime.Changed -= HandleRulesChanged;
            NetEvents.OnCountdown -= HandleCountdown;

            if (_aliveSubscribed && PlayerCombatState.Instance != null)
            {
                PlayerCombatState.Instance.AliveChanged -= HandleAliveChanged;
            }

            _aliveSubscribed = false;
            Instance = null;
        }

        private void Update()
        {
            if (Instance != this)
            {
                return;
            }

            // PlayerCombatState kendini sahne yüklendikten SONRA önyükler; Awake sırasında henüz
            // doğmamış olabilir (Weapon.TrySubscribeAlive ile aynı tembel abonelik deseni).
            if (!_aliveSubscribed)
            {
                TrySubscribeAlive();
            }

            if (!IsRandomGrant)
            {
                TickFrameSummon();
                return;
            }

            // Süpürme sahne yüklendiğinde yapılır; kurallar sahneden SONRA gelmişse (geç katılım)
            // burada telafi edilir.
            if (!_swept)
            {
                SweepScene();
            }

            // Ölüyken silah verilmez ve eldeki alınır: "hâlâ oynuyorum" hissi kalmasın (canlanma
            // zaten sabit durmayı istiyor — koşarken silah çekilmez).
            if (PlayerCombatState.Instance != null && !PlayerCombatState.Instance.IsAlive)
            {
                RevokeAll();
                return;
            }

            OVRCameraRig rig = ResolveRig();
            if (rig == null)
            {
                RevokeAll();
                return;
            }

            TickHand(OVRInput.Controller.LTouch, rig.leftHandAnchor, ref _grantedLeft);
            TickHand(OVRInput.Controller.RTouch, rig.rightHandAnchor, ref _grantedRight);
        }

        // ------------------------------------------------------------------ kural

        private static bool IsRandomGrant => ModeRuntime.Weapons == ModeWeaponSource.RandomGrant;

        private void HandleRulesChanged() => ApplyRules();

        private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            // Yeni sahne = yeni raf, yeni taban, yeni rig. Eldekiler eski sahneyle birlikte gitti.
            _swept = false;
            _hiddenObjects.Clear();
            _disabledZones.Clear();
            _grantedLeft = null;
            _grantedRight = null;
            _rig = null;

            // ⚠️ Çerçeve seçimi HARİTA başına sıfırlanır: "bu round boyunca aynı silah" kuralının
            // kapsamı bir haritadır. Yeni arenada oyuncu silahını yine kendi çerçevesinden alır —
            // seçim taşınsaydı o haritada bulunmayan bir silahla oynanabilirdi.
            _selected = null;
            DestroySummoned();

            ApplyRules();
        }

        /// <summary>Kural değişiminin tek uygulama noktası: RandomGrant'e girerken süpür, çıkarken
        /// geri al ve eldekileri temizle.</summary>
        private void ApplyRules()
        {
            // Silah KAYNAĞI değiştiyse çerçeve tarafı sıfırlanır — iki kaynak aynı anda elde silah
            // tutmasın. ⚠️ Koşul bilinçli: ModeRuntime.Changed aynı kaynakla da tekrar tekrar
            // yayınlanabilir (her load_match), koşulsuz sıfırlansaydı oyuncunun yarım şarjörü maçın
            // ortasında sessizce dolardı.
            bool random = IsRandomGrant;
            if (random != _appliedRandomGrant)
            {
                _appliedRandomGrant = random;
                _selected = null;
                DestroySummoned();
            }

            if (random)
            {
                SweepScene();
                return;
            }

            RestoreScene();
            RevokeAll();
        }

        // --------------------------------------------------------------- süpürme

        /// <summary>
        /// Raf silahlarını ve taban bölgelerini (<see cref="BaseZone"/>) gizler — bu modlarda
        /// canlanma şartı sabit durmaktır, şeritlerin ekranda kalması yanıltıcı olurdu.
        /// <para>
        /// ⚠️ <b>Bölgenin GameObject'i KAPATILMAZ, bileşeni kapatılır</b> + görsel şerit ayrıca
        /// gizlenir: yalnız bileşen kapatılsaydı şerit ekranda kalırdı, GameObject kapatılsaydı
        /// altına konmuş marker'lar (ör. <see cref="SpawnPoint"/>) <c>OnDisable</c>'da statik
        /// kayıttan düşerdi.
        /// </para>
        /// <para>
        /// Verilen silahlar süpürmeden MUAFTIR (<see cref="Weapon.IsGranted"/>) — süpürme
        /// <c>Weapon</c> bileşeni arıyor ve verilen örnek de bir <c>Weapon</c>.
        /// </para>
        /// </summary>
        private void SweepScene()
        {
            _swept = true;

            Weapon[] weapons = FindObjectsByType<Weapon>(FindObjectsSortMode.None);
            for (int i = 0; i < weapons.Length; i++)
            {
                Weapon weapon = weapons[i];
                if (weapon == null || weapon.IsGranted || !weapon.gameObject.activeSelf)
                {
                    continue;
                }

                weapon.gameObject.SetActive(false);
                _hiddenObjects.Add(weapon.gameObject);
            }

            BaseZone[] zones = FindObjectsByType<BaseZone>(FindObjectsSortMode.None);
            for (int i = 0; i < zones.Length; i++)
            {
                BaseZone zone = zones[i];
                if (zone == null)
                {
                    continue;
                }

                if (zone.enabled)
                {
                    zone.enabled = false;
                    _disabledZones.Add(zone);
                }

                HideBaseStrip(zone);
            }
        }

        /// <summary>Taban bölgesinin görsel şeridi: Renderer'lı doğrudan çocuklar.
        /// <para>⚠️ Alt ağacında <see cref="SpawnPoint"/> BULUNAN çocuğa dokunulmaz — arenanın
        /// tek başlangıç noktası şeridin torunu olarak konmuş olabilir ve kapatılırsa
        /// <c>OnDisable</c> ile statik kayıttan düşer. Kontrol bu yüzden <c>GetComponent</c>
        /// değil <c>GetComponentInChildren</c>'dır.</para></summary>
        private void HideBaseStrip(BaseZone zone)
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

        /// <summary>Süpürmeyi geri alır (kural RandomGrant'ten çıktı). Sahne değiştiyse listeler
        /// zaten boştur; ölü referanslar Unity null'ıyla atlanır.</summary>
        private void RestoreScene()
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
            _swept = false;
        }

        // ------------------------------------------------------------ silah verme

        /// <summary>Bir elin bir karelik durumu: grip basılıysa elde silah olsun, değilse olmasın.</summary>
        private void TickHand(OVRInput.Controller hand, Transform anchor, ref Weapon granted)
        {
            if (anchor == null)
            {
                Revoke(ref granted);
                return;
            }

            bool gripHeld = OVRInput.Get(OVRInput.Axis1D.PrimaryHandTrigger, hand) >= GripThreshold;

            if (!gripHeld)
            {
                Revoke(ref granted);
                return;
            }

            // Silah hâlâ elde (ve dışarıdan yok edilmemiş) — yapacak bir şey yok.
            if (granted != null)
            {
                return;
            }

            granted = Grant(hand, anchor);
        }

        /// <summary>Loadout'tan bir silahı el anchor'ının altına örnekler.
        /// <para>Ard arda aynı silahın gelmesi ENGELLENMEZ: iki silahlık bir havuzda "asla aynısı
        /// gelmesin" kuralı rastgeleliği sırayla-dağıtıma çevirirdi.</para></summary>
        private Weapon Grant(OVRInput.Controller hand, Transform anchor)
        {
            WeaponDefinition definition = PickFromLoadout();
            if (definition == null || definition.Prefab == null)
            {
                WarnMissingLoadout(definition);
                return null;
            }

            // §6.6 kanonik kavrama: duruş tanımın SABİT kavrama ofsetinden gelir (raftan kavranan
            // silah da aynı ofsetten sürülür — Weapon.ApplyCanonicalGrip). Buradaki fark yalnız
            // yöntem: verilen silah anchor'ın ÇOCUĞU olduğu için ofset yerel transformda yaşar.
            GameObject instance = Instantiate(definition.Prefab, anchor, false);
            instance.transform.localPosition = definition.PrimaryGripPosition;
            instance.transform.localRotation = definition.PrimaryGripRotation;
            instance.name = definition.Prefab.name;

            var weapon = instance.GetComponent<Weapon>();
            if (weapon == null)
            {
                Debug.LogWarning($"[WeaponGranter] '{definition.name}' prefabında Weapon bileşeni yok; silah verilemedi.");
                Destroy(instance);
                return null;
            }

            DetachFromPhysicsAndGrab(instance);

            // Çerçeveyi burada kapatmaya gerek YOK: WeaponFrame silahın `HeldChanged`'ini dinliyor
            // ve aşağıdaki GrantTo onu tetikliyor — kural tek yerde yaşasın.
            weapon.GrantTo(hand, WeaponGrantKind.Disposable);
            return weapon;
        }

        /// <summary>
        /// TEK KULLANIMLIK verilen silah elde SABİT durur: kavrama ve fizik yolları kapatılır.
        /// <para>
        /// Kapatılmasaydı silah (a) diğer elle ya da uzaktan kavranabilir, (b) yer çekimiyle elden
        /// düşer, (c) çarpışmalarıyla oyuncunun kendi ışınına takılırdı.
        /// </para>
        /// <para>
        /// ⚠️ Çerçeve klonunun karşılığı <see cref="PrepareSummonedClone"/>'dur ve <b>bilerek daha
        /// azını kapatır</b>: orada ön kabza ISDK kavramasına açık kalmalı, dolayısıyla
        /// Grabbable/GrabInteractable ve collider'lar AÇIK bırakılır.
        /// </para>
        /// </summary>
        private static void DetachFromPhysicsAndGrab(GameObject instance)
        {
            var interactables = instance.GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = 0; i < interactables.Length; i++)
            {
                MonoBehaviour behaviour = interactables[i];
                // ISDK'nın kavrama yüzeyi: Grabbable + (Distance)GrabInteractable. Weapon'ın
                // kendisi ve ses/efekt bileşenleri elbette AÇIK kalır.
                if (behaviour is Grabbable || behaviour is GrabInteractable || behaviour is DistanceGrabInteractable)
                {
                    behaviour.enabled = false;
                }
            }

            var bodies = instance.GetComponentsInChildren<Rigidbody>(true);
            for (int i = 0; i < bodies.Length; i++)
            {
                bodies[i].isKinematic = true;
                bodies[i].detectCollisions = false;
                bodies[i].useGravity = false;
            }

            var colliders = instance.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                colliders[i].enabled = false;
            }
        }

        /// <summary>
        /// Aktif modun loadout'undan bir silah seçer (prefabı olmayan tanımlar havuza girmez).
        /// Normalde RASTGELE; dev sandbox'ta <see cref="SequentialGrant"/> ile SIRAYLA.
        /// </summary>
        private WeaponDefinition PickFromLoadout()
        {
            _pool.Clear();

            GameCatalog catalog = Resources.Load<GameCatalog>("GameCatalog");
            ModeDefinition mode = catalog != null ? catalog.FindMode(ModeRuntime.ModeId) : null;
            WeaponDefinition[] loadout = mode != null ? mode.Loadout : null;
            if (loadout == null)
            {
                return null;
            }

            for (int i = 0; i < loadout.Length; i++)
            {
                if (loadout[i] != null && loadout[i].Prefab != null)
                {
                    _pool.Add(loadout[i]);
                }
            }

            if (_pool.Count == 0)
            {
                return null;
            }

#if UNITY_EDITOR
            if (SequentialGrant)
            {
                // İmleç havuz uzunluğuna göre sarılır; havuz iki Play arasında değişse bile
                // (moda silah eklendi) indeks aralık dışına düşmez.
                _sequentialIndex = (_sequentialIndex + 1) % _pool.Count;
                return _pool[_sequentialIndex];
            }
#endif

            return _pool[Random.Range(0, _pool.Count)];
        }

        private void WarnMissingLoadout(WeaponDefinition definition)
        {
            if (_loadoutWarned)
            {
                return;
            }

            _loadoutWarned = true;
            Debug.LogWarning(definition == null
                ? $"[WeaponGranter] '{ModeRuntime.ModeId}' modunun loadout'u boş (ya da katalogda yok); " +
                  "rastgele silah verilemiyor — ModeDefinition.loadout'a prefablı WeaponDefinition ekle."
                : $"[WeaponGranter] '{definition.name}' tanımının prefabı yok; rastgele silah verilemiyor.");
        }

        private void Revoke(ref Weapon granted)
        {
            if (granted != null)
            {
                Destroy(granted.gameObject);
            }

            granted = null;
        }

        private void RevokeAll()
        {
            Revoke(ref _grantedLeft);
            Revoke(ref _grantedRight);
        }

        // ------------------------------------------------------------- çerçeve silahı

        /// <summary>
        /// <see cref="WeaponFrame"/> çağırır: oyuncunun bu haritadaki silahını seçer.
        /// <para>
        /// Aynı silah yeniden seçilirse <b>hiçbir şey yapılmaz</b> — mevcut klon ve şarjörü korunur,
        /// yoksa oyuncu çerçevesine her nişan aldığında mermisi sessizce dolardı. Farklı bir silah
        /// seçilirse eski klon yok edilir ve yenisi bir sonraki grip'te TAM şarjörle doğar.
        /// </para>
        /// </summary>
        public static void SelectWeapon(WeaponDefinition definition)
        {
            if (Instance == null || definition == null)
            {
                return;
            }

            Instance.ApplySelection(definition);
        }

        private void ApplySelection(WeaponDefinition definition)
        {
            if (_selected == definition)
            {
                return;
            }

            _selected = definition;
            DestroySummoned();
        }

        /// <summary>
        /// Çerçeve yolunun bir karelik durumu: grip basılıysa seçili silahın klonu elde olsun,
        /// değilse gizlensin.
        /// <para>
        /// <b>Oyuncu başına TEK silah:</b> basılı olan İLK el kazanır; iki el de basılıysa silah
        /// hâlihazırda çağırılmış elde kalır (yoksa sağ el) — el değiştirme silahı ellerin arasında
        /// titretmesin.
        /// </para>
        /// <para>
        /// ⚠️ Ölünce klon <b>gizlenir ama SEÇİM KORUNUR</b>: canlanan oyuncu aynı silahla döner
        /// (dolumu <see cref="HandleAliveChanged"/> yapar). Seçim de silinseydi her ölümden sonra
        /// çerçeveye yürümek gerekirdi.
        /// </para>
        /// </summary>
        private void TickFrameSummon()
        {
            if (PlayerCombatState.Instance != null && !PlayerCombatState.Instance.IsAlive)
            {
                StowSummoned();
                return;
            }

            OVRCameraRig rig = ResolveRig();
            if (rig == null)
            {
                StowSummoned();
                return;
            }

            bool leftHeld = OVRInput.Get(OVRInput.Axis1D.PrimaryHandTrigger, OVRInput.Controller.LTouch) >= GripThreshold;
            bool rightHeld = OVRInput.Get(OVRInput.Axis1D.PrimaryHandTrigger, OVRInput.Controller.RTouch) >= GripThreshold;

            OVRInput.Controller hand = OVRInput.Controller.None;
            if (leftHeld && rightHeld)
            {
                hand = _summonedHand != OVRInput.Controller.None ? _summonedHand : OVRInput.Controller.RTouch;
            }
            else if (leftHeld)
            {
                hand = OVRInput.Controller.LTouch;
            }
            else if (rightHeld)
            {
                hand = OVRInput.Controller.RTouch;
            }

            if (hand == OVRInput.Controller.None)
            {
                StowSummoned();
                return;
            }

            if (_selected == null)
            {
                // Henüz bir çerçeveden silah seçilmedi: grip boşa basılır (uyarı da basılmaz,
                // maçın başında bu NORMAL durumdur).
                return;
            }

            Transform anchor = hand == OVRInput.Controller.LTouch ? rig.leftHandAnchor : rig.rightHandAnchor;
            if (anchor == null)
            {
                StowSummoned();
                return;
            }

            SummonTo(hand, anchor);
        }

        /// <summary>
        /// Seçili silahın klonunu <paramref name="hand"/>'e çağırır (gerekirse önce üretir).
        /// </summary>
        private void SummonTo(OVRInput.Controller hand, Transform anchor)
        {
            if (_summoned == null)
            {
                _summoned = CreateSummoned();
                if (_summoned == null)
                {
                    return;
                }
            }

            if (_summonedHand == hand && _summoned.gameObject.activeSelf)
            {
                return; // zaten aynı elde
            }

            // İlk kare için başlangıç pozu: klon DDOL kökünün altında park ediyor, poz yazılmasaydı
            // silah bir kare dünyanın orijininde görünürdü (Weapon.ApplyCanonicalGrip LateUpdate'te
            // devralır).
            _summoned.transform.SetPositionAndRotation(
                anchor.position + anchor.rotation * _selected.PrimaryGripPosition,
                anchor.rotation * _selected.PrimaryGripRotation);

            _summoned.gameObject.SetActive(true);
            _summoned.GrantTo(hand, WeaponGrantKind.Persistent);
            _summonedHand = hand;
        }

        /// <summary>
        /// Seçili tanımın prefabından kalıcı klonu üretir.
        /// <para>
        /// ⚠️ Klon <b><see cref="transform"/>'un altına park eder</b> (yani DDOL kökü), el
        /// anchor'ının ÇOCUĞU OLMAZ: pozunu <c>Weapon.ApplyCanonicalGrip</c> sürer. Anchor'ın altına
        /// konsaydı silah sahne geçişinde rig'le birlikte yok olur ve "gizlenip geri gelen tek
        /// örnek" kuralı çökerdi.
        /// </para>
        /// <para>
        /// Klondaki <see cref="WeaponFrame"/>'in GameObject'i yok edilir — çerçeve sahnede kalan
        /// KAYNAK silaha aittir, elde çerçeve olmaz.
        /// </para>
        /// </summary>
        private Weapon CreateSummoned()
        {
            if (_selected == null || _selected.Prefab == null)
            {
                // Uyarı bir kez: bu metot grip basılı olduğu HER karede denenir, koşulsuz loglamak
                // konsolu boğar ve asıl teşhis satırını görünmez yapardı.
                if (!_summonPrefabWarned)
                {
                    _summonPrefabWarned = true;
                    Debug.LogWarning($"[WeaponGranter] '{(_selected != null ? _selected.name : "(seçim yok)")}' " +
                                     "tanımının prefabı yok; çerçeveden seçilen silah çağrılamıyor.");
                }

                return null;
            }

            GameObject instance = Instantiate(_selected.Prefab, transform, false);
            instance.name = _selected.Prefab.name;

            var weapon = instance.GetComponent<Weapon>();
            if (weapon == null)
            {
                Debug.LogWarning($"[WeaponGranter] '{_selected.name}' prefabında Weapon bileşeni yok; " +
                                 "çerçeve silahı çağrılamadı.");
                Destroy(instance);
                return null;
            }

            var frame = instance.GetComponentInChildren<WeaponFrame>(true);
            if (frame != null)
            {
                frame.DetachForClone();
                Destroy(frame.gameObject);
            }

            PrepareSummonedClone(instance);
            return weapon;
        }

        /// <summary>
        /// Klonu ele hazırlar: fizik kapatılır ama <b>kavrama AÇIK bırakılır</b>.
        /// <para>
        /// Fark <see cref="DetachFromPhysicsAndGrab"/>'e göre bilinçlidir: çerçeve silahında ikinci
        /// el ön kabzayı ISDK ile tutar, dolayısıyla Grabbable/GrabInteractable ve collider'lar
        /// çalışır kalmalı (collider olmadan kavrama hiç algılanmaz).
        /// </para>
        /// <para>
        /// ⚠️ Bunlar burada <b>açıkça yeniden AÇILIR</b>, çünkü klon prefabtan doğarken içindeki
        /// <see cref="WeaponFrame"/>'in <c>Awake</c>'i çoktan koşmuş ve kavrama yollarını kapatmış
        /// olur (o bileşeni ancak doğduktan sonra yok edebiliyoruz — kaynak silahtaki doğru davranış
        /// klonda yanlış davranıştır).
        /// </para>
        /// </summary>
        private static void PrepareSummonedClone(GameObject instance)
        {
            var behaviours = instance.GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = 0; i < behaviours.Length; i++)
            {
                MonoBehaviour behaviour = behaviours[i];
                if (behaviour is Grabbable || behaviour is GrabInteractable || behaviour is ItemGripSockets)
                {
                    behaviour.enabled = true;
                }
                else if (behaviour is DistanceGrabInteractable)
                {
                    // Mesafeden kavrama soket tasarımının zıddıdır (Docs/Sistem-Ozeti.md §7);
                    // elde duran silahta hiç açılmaz.
                    behaviour.enabled = false;
                }
            }

            var bodies = instance.GetComponentsInChildren<Rigidbody>(true);
            for (int i = 0; i < bodies.Length; i++)
            {
                // Yer çekimi kapalı ve kinematik: silahın pozunu kanonik kavrama sürüyor, fizik
                // onunla yarışırsa silah elden düşer.
                bodies[i].isKinematic = true;
                bodies[i].useGravity = false;
            }
        }

        /// <summary>Klonu elden alır: YOK ETMEZ, yalnız gizler — mermisi ve durumu korunsun.</summary>
        private void StowSummoned()
        {
            if (_summoned != null)
            {
                _summoned.Revoke();
                _summoned.gameObject.SetActive(false);
            }

            _summonedHand = OVRInput.Controller.None;
        }

        /// <summary>Klonu tümden yok eder (silah değişti / harita değişti / kural değişti).</summary>
        private void DestroySummoned()
        {
            if (_summoned != null)
            {
                Destroy(_summoned.gameObject);
                _summoned = null;
            }

            _summonedHand = OVRInput.Controller.None;
        }

        private void TrySubscribeAlive()
        {
            if (_aliveSubscribed || PlayerCombatState.Instance == null)
            {
                return;
            }

            PlayerCombatState.Instance.AliveChanged += HandleAliveChanged;
            _aliveSubscribed = true;
        }

        /// <summary>
        /// Canlanan oyuncu TAM şarjörle döner.
        /// <para>
        /// ⚠️ Bunu <c>Weapon.HandleAliveChanged</c> YAPAMAZ: o <c>IsHeld</c> şartına bakıyor, oysa
        /// canlanma anında klon gizlidir (ölünce elden alındı) — yani silah hiç dolmaz ve oyuncu
        /// bir önceki hayatından kalan yarım şarjörle savaşa dönerdi.
        /// </para>
        /// </summary>
        private void HandleAliveChanged(bool alive)
        {
            if (alive && _summoned != null)
            {
                _summoned.RefillFull();
            }
        }

        /// <summary>
        /// Her geri sayımda (§10.1 <c>phaseReason:"countdown"</c>) elindeki silah tam dolar.
        /// <para>
        /// ⚠️ <see cref="HandleAliveChanged"/> tek başına YETMEZ: turu <b>sağ bitiren</b> oyuncu
        /// canlanmadığı için o olayı hiç almaz ve bir sonraki tura yarım şarjörle girerdi. Kapı
        /// geri sayım olduğu için mod-agnostiktir — tur tabanlı modda her tur, diğerlerinde maç
        /// başında bir kez çalışır (orada sahne zaten yeni yüklendiği için etkisizdir).
        /// </para>
        /// <para><see cref="Weapon.RefillFull"/> idempotenttir; geri sayımın her saniyesinde
        /// çağrılması zararsızdır (devam eden reload iptal olur, o da doğrudur).</para>
        /// </summary>
        private void HandleCountdown(CountdownMsg msg)
        {
            if (_summoned != null)
            {
                _summoned.RefillFull();
            }
        }

        // ------------------------------------------------------------ el çözümü (ISDK)

        /// <summary>
        /// Pointer olayını üreten interactor'dan elin OVR kontrolcüsünü çözer.
        /// <c>evt.Data</c> varsayılan olarak interactor'ın kendisidir (<c>Interactor._data</c>);
        /// BB kontrolcü rig'i interactor↔IController eşlemesini
        /// <see cref="InteractorControllerDecorator"/> ile kurar. Çözülemezse
        /// <see cref="OVRInput.Controller.None"/> döner (editör fallback işareti).
        /// <para>
        /// ⚠️ <b>El çözümünün TEK yeri burasıdır</b> ve kopyalanmaz: üç tüketicisi var
        /// (<see cref="Weapon"/>, <see cref="ItemGripSockets"/>, <see cref="WeaponFrame"/>) ve
        /// kopyalandığı sürece biri düzeltilip diğerleri unutuluyordu. Burada durmasının sebebi de
        /// rig keşfiyle aynı: el ve anchor aynı kapıdan çözülsün.
        /// </para>
        /// </summary>
        public static OVRInput.Controller ResolveController(in PointerEvent evt)
        {
            if (evt.Data is IInteractorView view &&
                InteractorControllerDecorator.TryGetControllerForInteractor(view, out IController controller))
            {
                return ToOvrController(controller.Handedness);
            }

            // Yedek: decorator kurulu değilse interactor hiyerarşisindeki ControllerRef'e bak.
            Component dataComponent = evt.Data as Component;
            if (dataComponent != null)
            {
                ControllerRef controllerRef = dataComponent.GetComponentInParent<ControllerRef>();
                if (controllerRef != null)
                {
                    return ToOvrController(controllerRef.Handedness);
                }
            }

            return OVRInput.Controller.None;
        }

        /// <summary>
        /// Interactor'ın GameObject'inden eli çözer — <see cref="ResolveController(in PointerEvent)"/>
        /// ile AYNI yol (decorator, yoksa hiyerarşideki <see cref="ControllerRef"/>). ISDK filtreleri
        /// (<c>IGameObjectFilter.Filter</c>) olay değil GameObject aldığı için ikinci imza var.
        /// </summary>
        public static OVRInput.Controller ResolveControllerFromGameObject(GameObject interactorGameObject)
        {
            if (interactorGameObject == null)
            {
                return OVRInput.Controller.None;
            }

            var view = interactorGameObject.GetComponent<IInteractorView>();
            if (view != null &&
                InteractorControllerDecorator.TryGetControllerForInteractor(view, out IController controller))
            {
                return ToOvrController(controller.Handedness);
            }

            ControllerRef controllerRef = interactorGameObject.GetComponentInParent<ControllerRef>();
            if (controllerRef != null)
            {
                return ToOvrController(controllerRef.Handedness);
            }

            return OVRInput.Controller.None;
        }

        private static OVRInput.Controller ToOvrController(Handedness handedness)
        {
            return handedness == Handedness.Left ? OVRInput.Controller.LTouch : OVRInput.Controller.RTouch;
        }

        // ---------------------------------------------------------------- yardımcı

        /// <summary>
        /// El anchor'ı çözmenin <b>TEK</b> yolu: verilen silah da (bkz. <see cref="Grant"/>),
        /// raftan kavranan silah da (<c>Weapon.ApplyCanonicalGrip</c>) buradan geçer.
        /// <para>
        /// ⚠️ İkinci bir rig keşif yolu YAZILMAZ: iki ayrı arama farklı karelerde farklı rig
        /// bulabilir (sahne geçişi, gözlemcinin kapattığı rig) ve silah bir karede el değiştirmiş
        /// gibi zıplardı. Rig yoksa <c>null</c> döner — çağıran hiçbir şey yapmaz.
        /// </para>
        /// <para><see cref="OVRInput.Controller.None"/> için de <c>null</c> döner: "el çözülemedi"
        /// durumunda sessizce sağ ele yapıştırmak silahı yanlış elde gösterirdi.</para>
        /// </summary>
        public static Transform ResolveHandAnchor(OVRInput.Controller hand)
        {
            if (hand != OVRInput.Controller.LTouch && hand != OVRInput.Controller.RTouch)
            {
                return null;
            }

            OVRCameraRig rig = Instance != null ? Instance.ResolveRig() : null;
            if (rig == null)
            {
                return null;
            }

            return hand == OVRInput.Controller.LTouch ? rig.leftHandAnchor : rig.rightHandAnchor;
        }

        /// <summary>BB rig'i (aktif olan) bulur; bulunamazsa saniyede bir yeniden dener.
        /// Admin gözlemcide rig KAPALI olduğu için burası kalıcı olarak null döner.</summary>
        private OVRCameraRig ResolveRig()
        {
            if (_rig != null && _rig.isActiveAndEnabled)
            {
                return _rig;
            }

            if (Time.time < _nextRigScanAt)
            {
                return null;
            }

            _nextRigScanAt = Time.time + RigRescanSeconds;
            _rig = FindFirstObjectByType<OVRCameraRig>();
            return _rig;
        }
    }
}
