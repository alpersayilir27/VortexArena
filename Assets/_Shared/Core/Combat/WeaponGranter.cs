using System.Collections.Generic;
using Oculus.Interaction;
using Oculus.Interaction.HandGrab;
using Oculus.Interaction.Input;
using UnityEngine;
using UnityEngine.SceneManagement;
using VortexArena.Core.Arena;
using VortexArena.Core.Player;
using VortexArena.Net;
using VortexArena.Protocol;

namespace VortexArena.Core.Combat
{
    /// <summary>
    /// Oyuncunun eline silah koyan <b>TEK</b> yer — iki ayrı kaynaktan beslenir ve hangisinin
    /// işlediğini mod kuralı (<see cref="ModeWeaponSource"/>) belirler:
    /// <list type="number">
    /// <item><b>RandomGrant</b> (§10.5 <c>weaponSource:"random"</c>): sahnedeki silahları SÜPÜRÜR
    /// ve grip'e basılı tutulan her elde loadout'tan rastgele bir silah durur;
    /// bırakılınca <b>yok olur</b>, tekrar basınca YENİSİ gelir (<see cref="WeaponGrantKind.Disposable"/>).</item>
    /// <item><b>Çerçeve</b> (<see cref="WeaponFrame"/>, <see cref="ModeWeaponSource.WeaponCanvas"/>): silah
    /// sahnede çerçevesinde sabit durur, oyuncu onu <see cref="WeaponFrame"/>'in
    /// <c>maxGrabDistance</c>'ı kadar uzaktan SEÇER
    /// (<see cref="SelectWeapon"/>); grip'e basınca seçilen silahın KLONU eline gelir, bırakınca
    /// <b>yalnız gizlenir</b> — aynı örnek aynı mermiyle geri gelir
    /// (<see cref="WeaponGrantKind.Persistent"/>). Çift elli silahta oyuncu başına TEK klon; tek
    /// elli silahta EL BAŞINA bir klon (çift tabanca).</item>
    /// </list>
    /// <para>
    /// ⚠️ <b>Silahı sahneye koyan bir bileşen YOKTUR ve yazılmaz</b> — <c>WeaponCanvas</c>
    /// kaynağında yerleşim <b>arena kararıdır</b>: silahlar harita tasarlanırken elle konur
    /// (<c>BaseZone</c> gibi bir prefab örneği olarak). Bu sınıf yalnız ALMA yolunu bilir;
    /// sahnede silah yoksa sessizce eline bir şey gelmez, hata değildir.
    /// </para>
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
    /// etmez) → el anchor'ı yok → silah verilmez. Süpürme ise role bakmaz: sahnede duran silah
    /// FFA'da gözlemcinin ekranında da durmamalı.
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

        /// <summary>
        /// Seçili silahın KALICI klonu, EL BAŞINA: bırakılınca yok edilmez, yalnız gizlenir —
        /// "aynı silah aynı mermiyle geri gelir" kuralı bu örneklerden doğar.
        /// <para>
        /// ⚠️ <b>İki slot olması "her zaman iki klon" demek DEĞİLDİR:</b> ikinci klon yalnız seçili
        /// silah TEK elliyken üretilir (çift tabanca). Çift elli silahta tek klon vardır ve el
        /// değiştirdiğinde slotlar arasında TAŞINIR — yeniden üretmek şarjörü sıfırlardı.
        /// </para>
        /// </summary>
        private Weapon _summonedLeft;
        private Weapon _summonedRight;

        private OVRCameraRig _rig;
        private float _nextRigScanAt;
        private bool _aliveSubscribed;

        /// <summary>Süpürmenin GİZLEDİĞİ objeler — kural değişince geri açılabilsin diye tutulur.
        /// Sahne değişince referanslar ölür (Unity null'ı) ve liste yeniden kurulur.</summary>
        private readonly List<GameObject> _hiddenObjects = new List<GameObject>();

        /// <summary>Rastgele seçimin havuzu — her karede yeni liste ayırmamak için tampon.</summary>
        private readonly List<WeaponDefinition> _pool = new List<WeaponDefinition>();

        private bool _swept;
        private bool _loadoutWarned;
        private bool _summonPrefabWarned;

        /// <summary>Son uygulanan silah kaynağı — yalnız GEÇİŞTE çerçeve durumunu sıfırlamak için
        /// (bkz. <see cref="ApplyRules"/>).</summary>
        private bool _appliedRandomGrant;

        /// <summary>Son uygulanan "mod silahı dağıtıyor" durumu (<see cref="ModeDistributesWeapons"/>).
        /// <para>⚠️ <see cref="_appliedRandomGrant"/> tek başına YETMEZ: sahnelenmiş arenadan FFA
        /// maçına geçişte silah kaynağı <c>random</c> kalır (ikisi de öyle) ve yalnız bu bayrak
        /// değişir — izlenmeseydi oyuncunun sahnelemede tezgâhtan seçtiği silah maça taşınır,
        /// yani "modun dağıttığı rastgele silah" kuralı sessizce delinirdi.</para></summary>
        private bool _appliedModeDistributes;

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

            // Çerçeve yolu iki durumda koşar: (1) modun silah kaynağı tezgâh, (2) oyuncu bir
            // tezgâhtan silah SEÇTİ. İkincisi yalnız serbest alanda mümkündür — koşan bir maçta
            // tezgâhlar gizlidir, dolayısıyla seçilemezler.
            // ⚠️ RevokeAll koşulsuzdur: serbest alanda iki yol da açık olduğu için oyuncu tezgâhtan
            // seçtiği anda elinde rastgele verilmiş bir silah duruyor olabilir; alınmasaydı iki
            // kaynaktan iki silah birden taşırdı. Çerçeve kaynağında zaten no-op'tur (o yolda
            // _grantedLeft/Right hiç dolmaz).
            if (!IsRandomGrant || _selected != null)
            {
                RevokeAll();
                TickFrameSummon();
                return;
            }

            // Süpürme sahne yüklendiğinde yapılır; kurallar sahneden SONRA gelmişse (geç katılım)
            // burada telafi edilir. ⚠️ Serbest alanda süpürülmez (gerekçe SweepScene'de).
            if (!_swept && ModeDistributesWeapons)
            {
                SweepScene();
            }

            if (!CanHoldWeapon)
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

            // ⚠️ Her ele ÖTEKİ elin silahı da verilir: bir eldeki silah çift elliyse öteki el ikinci
            // silah ALMAZ, ön kabzaya aday olur (bkz. TickHand).
            TickHand(OVRInput.Controller.LTouch, rig.leftHandAnchor, ref _grantedLeft, _grantedRight);
            TickHand(OVRInput.Controller.RTouch, rig.rightHandAnchor, ref _grantedRight, _grantedLeft);
        }

        // ------------------------------------------------------------------ kural

        private static bool IsRandomGrant => ModeRuntime.Weapons == ModeWeaponSource.RandomGrant;

        /// <summary>
        /// Kurulmuş bir maç YOK — oyuncu serbest alanda (lobi profili, §10.7).
        /// <para>
        /// ⚠️ <b>Bu "lobi SAHNESİNDEYİZ" demek DEĞİLDİR:</b> operatör lobideyken bir arena
        /// seçtiğinde sunucu o arenayı <b>sahneler</b> — herkes arenaya geçer ama tür
        /// <c>lobby</c> ve kural şekli lobi profilinde kalır (maç ancak <c>start_match</c> ile
        /// kurulur). Yani serbest alan çoğu zaman <b>silah tezgâhı olan bir arenadır</b>.
        /// </para>
        /// <para>
        /// ⚠️ Ayrım <c>modeId</c>'den DEĞİL kuraldan okunur (§10.5: istemcide
        /// <c>if (modeId == "lobby")</c> zinciri yazılmaz). Lobi profilini benzersiz kılan bileşim
        /// <c>random</c> + <c>fireWhilePaused</c>'dur: koşan bir FFA maçı da <c>random</c>'dır ama
        /// serbest atışı yoktur, dolayısıyla ikisi karışmaz.
        /// </para>
        /// </summary>
        private static bool IsFreePlayground => IsRandomGrant && ModeRuntime.FireWhilePaused;

        /// <summary>Silahı MOD dağıtıyor, yani sahnedeki tezgâhlar gizlenmeli: kaynak
        /// <c>random</c> <b>ve</b> ortada kurulmuş bir maç var (FFA). Serbest alan bunun dışındadır.</summary>
        private static bool ModeDistributesWeapons => IsRandomGrant && !IsFreePlayground;

        /// <summary>
        /// Oyuncu şu an elinde silah TUTABİLİR mi: <b>canlı</b> ve <b>kalibreli</b> olmalı.
        /// <para>
        /// Ölümde gerekçe "hâlâ oynuyorum" hissinin kalmamasıdır (canlanma zaten sabit durmayı
        /// istiyor). Kalibresizlikte gerekçe oyuncunun zaten ateş EDEMİYOR olmasıdır (§10.6,
        /// <c>PlayerCombatState.CanFire</c>): elinde tetiği çekilen ama hiçbir şey yapmayan bir
        /// silah tutmak, oyuncuya sorunun silahta olduğunu düşündürürdü.
        /// </para>
        /// <para>
        /// ⚠️ <b>Silahın ele geldiği İKİ yol da bu tek kapıdan geçer</b> (rastgele dağıtım ve
        /// çerçeve klonu) — biri kapatılıp diğeri açık bırakılsaydı kural moda göre sessizce
        /// delinirdi. Üçüncü yol olan çerçeveden SEÇİM ayrı kapatılır
        /// (<see cref="SelectWeapon"/> + <see cref="CanSelect"/>).
        /// </para>
        /// <para>
        /// Kalibrasyon durumu kare başına <b>yoklanır</b>, olaya abone olunmaz: bu sınıfın döngüsü
        /// zaten her kare koşuyor, yani durum bozulduğu kare silah elden gider ve ikinci bir
        /// abonelik ömrü yönetmek gerekmez.
        /// </para>
        /// </summary>
        private static bool CanHoldWeapon =>
            (PlayerCombatState.Instance == null || PlayerCombatState.Instance.IsAlive) &&
            CalibrationState.IsCalibrated;

        private void HandleRulesChanged() => ApplyRules();

        private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            // Yeni sahne = yeni silahlar, yeni rig.
            _swept = false;
            _hiddenObjects.Clear();
            _rig = null;

            // ⚠️ Verilen silahlar DDOL kökünün altında park ediyor (bkz. Grant): sahne geçişinde
            // kendiliğinden ölmezler, açıkça yok edilmeliler — referansı null'lamak eski sahnenin
            // silahını elde bırakırdı.
            RevokeAll();

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
            bool distributes = ModeDistributesWeapons;

            if (random != _appliedRandomGrant || distributes != _appliedModeDistributes)
            {
                _appliedRandomGrant = random;
                _appliedModeDistributes = distributes;
                _selected = null;
                DestroySummoned();
            }

            if (distributes)
            {
                SweepScene();
                return;
            }

            RestoreScene();
            RevokeAll();
        }

        // --------------------------------------------------------------- süpürme

        /// <summary>
        /// Sahnede duran silahları gizler: silahı mod dağıtıyorsa sahnedeki örnekler oyuncuya
        /// alınabilirmiş gibi görünmemeli.
        /// <para>
        /// ⚠️ <b>Yalnız KURULMUŞ bir maçta koşar</b> (<see cref="ModeDistributesWeapons"/>), maç
        /// yokken DEĞİL. Sebep: operatör lobideyken bir arena seçince o arena sahnelenir ve kural
        /// şekli lobi profilinde kalır (<c>random</c>) — kapı yalnız "kaynak random mı" olsaydı,
        /// maç başlamadan arenanın silah tezgâhları gizlenirdi ve oyuncu bekleme süresince ne silah
        /// alabilir ne de serbest atış yapabilirdi (oysa lobi profilinin bütün amacı odur).
        /// Serbest alanda iki yol birden açıktır: tezgâhtan seçilirse o silah, seçilmezse
        /// loadout'tan rastgele biri gelir.
        /// </para>
        /// <para>
        /// ⚠️ <b>Taban bölgeleri BURADA DEĞİLDİR</b> ve buraya geri eklenmez — şeritlerin
        /// görünürlüğü silah kaynağına değil takım kipine bağlıdır ve
        /// <see cref="Arena.BaseZoneVisibility"/>'e aittir. İkisi eskiden aynı süpürmedeydi; FFA'da
        /// birlikte değiştikleri için doğru görünüyordu, lobinin silahı rastgeleye alınınca
        /// lobideki tabanlar da kayboldu.
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

            _hiddenObjects.Clear();
            _swept = false;
        }

        // ------------------------------------------------------------ silah verme

        /// <summary>
        /// Bir elin bir karelik durumu: grip basılıysa elde silah olsun, değilse olmasın.
        /// <para>
        /// ⚠️ <b>Öteki eldeki silah ÇİFT ELLİYSE bu ele ikinci bir silah VERİLMEZ</b>; o el ön
        /// kabzaya adaydır (grip basılı + avuç ikincil soketin yarıçapında). Aksi hâlde oyuncu FFA'da
        /// iki tüfek tutar ve tüfeği iki elle sabitlemenin hiçbir yolu kalmazdı.
        /// </para>
        /// </summary>
        private void TickHand(OVRInput.Controller hand, Transform anchor, ref Weapon granted,
            Weapon otherHandWeapon)
        {
            if (anchor == null)
            {
                Revoke(ref granted);
                return;
            }

            bool gripHeld = OVRInput.Get(OVRInput.Axis1D.PrimaryHandTrigger, hand) >= GripThreshold;

            if (IsTwoHandHeld(otherHandWeapon))
            {
                Revoke(ref granted);
                otherHandWeapon.SetSecondaryHand(
                    gripHeld && IsPalmOnSecondarySocket(otherHandWeapon, hand)
                        ? hand
                        : OVRInput.Controller.None);
                return;
            }

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

            granted = Grant(hand);
        }

        /// <summary>Loadout'tan bir silahı ele verir.
        /// <para>Ard arda aynı silahın gelmesi ENGELLENMEZ: iki silahlık bir havuzda "asla aynısı
        /// gelmesin" kuralı rastgeleliği sırayla-dağıtıma çevirirdi.</para></summary>
        private Weapon Grant(OVRInput.Controller hand)
        {
            WeaponDefinition definition = PickFromLoadout();
            if (definition == null || definition.Prefab == null)
            {
                WarnMissingLoadout(definition);
                return null;
            }

            // ⚠️ Örnek el anchor'ının DEĞİL bu tekilin (DDOL kökü) altına kurulur ve pozunu her
            // karede Weapon.ApplyCanonicalGrip sürer — Persistent klonla AYNI desen. Anchor'ın
            // çocuğu olsaydı iki elli çözümün yazdığı dünya pozu parent dönüşümüyle çakışırdı.
            GameObject instance = Instantiate(definition.Prefab, transform, false);
            instance.name = definition.Prefab.name;

            // İlk kare için başlangıç pozu: kanonik kavrama LateUpdate'te devralır, poz yazılmasaydı
            // silah bir kare dünyanın orijininde görünürdü.
            if (TryResolvePalm(hand, out Pose palm))
            {
                ItemGripSolver.Solve(definition, palm, false, Vector3.zero, 0f,
                    out Vector3 position, out Quaternion rotation);
                instance.transform.SetPositionAndRotation(position, rotation);
            }

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
        /// ⚠️ Çerçeve klonunun karşılığı <see cref="PrepareSummonedClone"/>'dur ve kavrama tarafında
        /// AYNI kapıyı uygular; farkı yalnız soket göstergesini açık bırakmasıdır. İkinci elin
        /// algısı iki yolda da granter'ındır (<see cref="Weapon.SetSecondaryHand"/>).
        /// </para>
        /// </summary>
        private static void DetachFromPhysicsAndGrab(GameObject instance)
        {
            var interactables = instance.GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = 0; i < interactables.Length; i++)
            {
                MonoBehaviour behaviour = interactables[i];
                // ISDK'nın kavrama yüzeyi: Grabbable + kumanda hattı ((Distance)GrabInteractable)
                // + el hattı ((Distance)HandGrabInteractable). Weapon'ın kendisi ve ses/efekt
                // bileşenleri elbette AÇIK kalır. ⚠️ İki hat da kapatılır: hangisinin koşacağını
                // ISDK rig'i seçiyor, biri unutulursa verilen silah kavranabilir kalırdı.
                if (behaviour is Grabbable || behaviour is GrabInteractable ||
                    behaviour is DistanceGrabInteractable ||
                    behaviour is HandGrabInteractable || behaviour is DistanceHandGrabInteractable)
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
        /// <para>
        /// ⚠️ <b>Seçimi kısıtlayan bir kapı YOKTUR ve eklenmez</b> — "elde çift elli silah varken
        /// başkası seçilemesin" kuralı buraya AİT DEĞİLDİR. Çerçeve bir kavrama değil bir SEÇİM
        /// tetikleyicisidir ve seçim değiştirmek ikinci bir silah üretmez: çift elli seçimde
        /// oyuncu başına zaten TEK klon tutulur (<see cref="TickFrameSummon"/>), yeni tanım
        /// eskisinin yerine geçer. Böyle bir kapı rafta silah değiştirmeyi de kapatır ve belirtisi
        /// teşhisi zordur: <b>ışın çıkar ama seçim olmaz</b> — grip'e basıldığı anda bu sınıf eski
        /// silahın klonunu ele çağırır, kapı tam o karede kapanır ve oyuncu ilk aldığı silaha
        /// ömür boyu kilitlenir. "Aynı anda ikinci silah tutulamaz" kuralının yeri
        /// <see cref="TickHand"/>'dir (rastgele dağıtım yolu, öteki el).
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

        /// <summary>Bu silah şu an elde ve tanımı çift elli mi.</summary>
        private static bool IsTwoHandHeld(Weapon weapon)
        {
            return weapon != null && weapon.IsHeld &&
                   weapon.Definition != null && weapon.Definition.IsTwoHanded;
        }

        /// <summary>
        /// Bu elin AVUCU silahın ikincil soketinin kabul yarıçapında mı — ön kabza kavramasının
        /// kapısı. Ölçü <see cref="ItemGripSockets"/>'in çizim/kapı ölçüsüyle aynıdır
        /// (<c>SecondaryGripRadius</c>): oyuncuya "şimdi bas" diye yeşile dönen soket sessizce
        /// reddedilmesin.
        /// </summary>
        private static bool IsPalmOnSecondarySocket(Weapon weapon, OVRInput.Controller hand)
        {
            ItemDefinition definition = weapon != null ? weapon.Definition : null;
            if (definition == null || !definition.IsTwoHanded)
            {
                return false;
            }

            if (!TryResolvePalm(hand, out Pose palm))
            {
                return false;
            }

            // TransformPoint DEĞİL: soket ofseti METRE cinsindendir, ölçeklenmez.
            Transform item = weapon.transform;
            Vector3 socket = item.position + item.rotation * definition.SecondaryGripPosition;
            float radius = definition.SecondaryGripRadius;
            return (palm.position - socket).sqrMagnitude <= radius * radius;
        }

        private void ApplySelection(WeaponDefinition definition)
        {
            if (_selected == definition)
            {
                return;
            }

            // ⚠️ Burada bir "alabilir mi" kapısı YOKTUR (gerekçe SelectWeapon'da): seçim her zaman
            // uygulanır. Eldeki klon yok edilir; grip hâlâ basılıysa bir sonraki karede YENİ silahın
            // klonu doğar, yani raf değişimi tek karelik bir boşlukla tamamlanır.
            _selected = definition;
            DestroySummoned();
        }

        /// <summary>
        /// Çerçeve yolunun bir karelik durumu: grip basılıysa seçili silahın klonu elde olsun,
        /// değilse gizlensin.
        /// <para>
        /// <b>Klon sayısı seçili silahın tutuş kipinden gelir:</b>
        /// <list type="bullet">
        /// <item><b>Çift elli</b> (tüfek): oyuncu başına TEK klon — basılı olan İLK el kazanır, iki
        /// el de basılıysa silah hâlihazırda çağırılmış elde kalır (yoksa sağ el), yani el
        /// değiştirme silahı ellerin arasında titretmez. Boş elin grip'i ön kabzada ise o el ikinci
        /// eldir.</item>
        /// <item><b>Tek elli</b> (tabanca): her el KENDİ klonunu alır — iki ayrı örnek, iki ayrı
        /// şarjör. "Aynı silah aynı mermiyle geri gelir" kuralı el başına yürür.</item>
        /// </list>
        /// </para>
        /// <para>
        /// ⚠️ Ölünce klon <b>gizlenir ama SEÇİM KORUNUR</b>: canlanan oyuncu aynı silahla döner
        /// (dolumu <see cref="HandleAliveChanged"/> yapar). Seçim de silinseydi her ölümden sonra
        /// çerçeveye yürümek gerekirdi.
        /// </para>
        /// </summary>
        private void TickFrameSummon()
        {
            if (!CanHoldWeapon)
            {
                StowAllSummoned();
                return;
            }

            if (ResolveRig() == null)
            {
                StowAllSummoned();
                return;
            }

            bool leftHeld = OVRInput.Get(OVRInput.Axis1D.PrimaryHandTrigger, OVRInput.Controller.LTouch) >= GripThreshold;
            bool rightHeld = OVRInput.Get(OVRInput.Axis1D.PrimaryHandTrigger, OVRInput.Controller.RTouch) >= GripThreshold;

            if (!leftHeld && !rightHeld)
            {
                StowAllSummoned();
                return;
            }

            if (_selected == null)
            {
                // Henüz bir çerçeveden silah seçilmedi: grip boşa basılır (uyarı da basılmaz,
                // maçın başında bu NORMAL durumdur).
                return;
            }

            if (_selected.IsTwoHanded)
            {
                TickTwoHandSummon(leftHeld, rightHeld);
                return;
            }

            TickOneHandSummon(OVRInput.Controller.LTouch, leftHeld);
            TickOneHandSummon(OVRInput.Controller.RTouch, rightHeld);
        }

        /// <summary>Çift elli seçimde tek klon: ana el seçilir, boş el ön kabzaya adaydır.</summary>
        private void TickTwoHandSummon(bool leftHeld, bool rightHeld)
        {
            OVRInput.Controller hand;
            if (leftHeld && rightHeld)
            {
                hand = _summonedLeft != null ? OVRInput.Controller.LTouch : OVRInput.Controller.RTouch;
            }
            else
            {
                hand = leftHeld ? OVRInput.Controller.LTouch : OVRInput.Controller.RTouch;
            }

            OVRInput.Controller other = Opposite(hand);

            // El değiştiyse AYNI örnek öteki slottan taşınır: yeniden üretmek şarjörü sıfırlardı.
            Weapon weapon = GetSummoned(hand);
            if (weapon == null)
            {
                weapon = GetSummoned(other);
                SetSummoned(other, null);
            }

            if (weapon == null)
            {
                weapon = CreateSummoned();
                if (weapon == null)
                {
                    return;
                }
            }

            SetSummoned(hand, weapon);
            SummonTo(weapon, hand);

            // ⚠️ Ön kabza SummonTo'dan SONRA yazılır: GrantTo ikinci eli temizliyor (silah el
            // değiştirmiş olabilir), ters sırada yazılan değer aynı karede silinirdi.
            bool otherGrip = OVRInput.Get(OVRInput.Axis1D.PrimaryHandTrigger, other) >= GripThreshold;
            weapon.SetSecondaryHand(
                otherGrip && IsPalmOnSecondarySocket(weapon, other) ? other : OVRInput.Controller.None);
        }

        /// <summary>Tek elli seçimde her el kendi klonunu taşır (çift tabanca).</summary>
        private void TickOneHandSummon(OVRInput.Controller hand, bool gripHeld)
        {
            if (!gripHeld)
            {
                StowSummoned(hand);
                return;
            }

            Weapon weapon = GetSummoned(hand);
            if (weapon == null)
            {
                weapon = CreateSummoned();
                if (weapon == null)
                {
                    return;
                }

                SetSummoned(hand, weapon);
            }

            SummonTo(weapon, hand);
        }

        /// <summary>
        /// Klonu <paramref name="hand"/>'e çağırır (zaten oradaysa no-op).
        /// </summary>
        private void SummonTo(Weapon weapon, OVRInput.Controller hand)
        {
            if (weapon.GrantedHand == hand && weapon.gameObject.activeSelf)
            {
                return; // zaten aynı elde
            }

            // İlk kare için başlangıç pozu: klon DDOL kökünün altında park ediyor, poz yazılmasaydı
            // silah bir kare dünyanın orijininde görünürdü (Weapon.ApplyCanonicalGrip LateUpdate'te
            // devralır).
            if (TryResolvePalm(hand, out Pose palm))
            {
                ItemGripSolver.Solve(_selected, palm, false, Vector3.zero, 0f,
                    out Vector3 position, out Quaternion rotation);
                weapon.transform.SetPositionAndRotation(position, rotation);
            }

            weapon.gameObject.SetActive(true);
            weapon.GrantTo(hand, WeaponGrantKind.Persistent);
        }

        private Weapon GetSummoned(OVRInput.Controller hand)
        {
            return hand == OVRInput.Controller.LTouch ? _summonedLeft : _summonedRight;
        }

        private void SetSummoned(OVRInput.Controller hand, Weapon weapon)
        {
            if (hand == OVRInput.Controller.LTouch)
            {
                _summonedLeft = weapon;
            }
            else
            {
                _summonedRight = weapon;
            }
        }

        private static OVRInput.Controller Opposite(OVRInput.Controller hand)
        {
            return hand == OVRInput.Controller.LTouch
                ? OVRInput.Controller.RTouch
                : OVRInput.Controller.LTouch;
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
        /// Klonu ele hazırlar: fizik ve <b>kavramanın TAMAMI kapatılır</b>, yalnız soket GÖRSELİ
        /// (<see cref="ItemGripSockets"/>) açık kalır.
        /// <para>
        /// ⚠️ <b>Kavrama bileşenleri bilerek AÇILMAZ:</b> verilen silahta ikinci elin algısının TEK
        /// kaynağı granter'ın grip yoklamasıdır (<see cref="Weapon.SetSecondaryHand"/>). ISDK hattı
        /// da açık bırakılsaydı aynı el iki ayrı kaynaktan "tutuyor" görünür, telde
        /// <c>GRIP_LINKED</c> iki ayrı yoldan yazılır ve biri bırakınca durum yarım kalırdı.
        /// </para>
        /// <para>
        /// <see cref="ItemGripSockets"/> AÇIK kalır çünkü o bir kavrama yolu değil bir GÖSTERGEDİR:
        /// oyuncunun ön kabzayı nereden tutacağını görmesi gerekir (kapı işlevi verilen silahta
        /// artık boşa çalışır, çizim işlevi değil).
        /// </para>
        /// </summary>
        private static void PrepareSummonedClone(GameObject instance)
        {
            var behaviours = instance.GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = 0; i < behaviours.Length; i++)
            {
                MonoBehaviour behaviour = behaviours[i];
                if (behaviour is ItemGripSockets)
                {
                    // ⚠️ Açıkça AÇILIR: klon prefabtan doğarken içindeki WeaponFrame'in Awake'i
                    // çoktan koşmuş ve göstergeyi kapatmış olur (kaynak silahtaki doğru davranış
                    // klonda yanlış davranıştır).
                    behaviour.enabled = true;
                }
                else if (behaviour is Grabbable || behaviour is GrabInteractable ||
                         behaviour is HandGrabInteractable ||
                         behaviour is DistanceGrabInteractable || behaviour is DistanceHandGrabInteractable)
                {
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

        /// <summary>Bir elin klonunu elden alır: YOK ETMEZ, yalnız gizler — mermisi ve durumu
        /// korunsun.</summary>
        private void StowSummoned(OVRInput.Controller hand)
        {
            Weapon weapon = GetSummoned(hand);
            if (weapon == null)
            {
                return;
            }

            weapon.Revoke();
            weapon.gameObject.SetActive(false);
        }

        private void StowAllSummoned()
        {
            StowSummoned(OVRInput.Controller.LTouch);
            StowSummoned(OVRInput.Controller.RTouch);
        }

        /// <summary>Klonları tümden yok eder (silah değişti / harita değişti / kural değişti).</summary>
        private void DestroySummoned()
        {
            if (_summonedLeft != null)
            {
                Destroy(_summonedLeft.gameObject);
                _summonedLeft = null;
            }

            if (_summonedRight != null)
            {
                Destroy(_summonedRight.gameObject);
                _summonedRight = null;
            }
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
            if (alive)
            {
                RefillSummoned();
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
            RefillSummoned();
        }

        /// <summary>Her iki el klonunu da tam doldurur (tek elli seçimde iki ayrı örnek olabilir).</summary>
        private void RefillSummoned()
        {
            if (_summonedLeft != null)
            {
                _summonedLeft.RefillFull();
            }

            if (_summonedRight != null)
            {
                _summonedRight.RefillFull();
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
        /// sahneden kavranan silah da (<c>Weapon.ApplyCanonicalGrip</c>) buradan geçer.
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

        /// <summary>
        /// Elin AVUÇ pozu (<see cref="ResolveHandAnchor"/> + <see cref="HandGripPivot"/>); rig ya da
        /// el çözülemezse <c>false</c>.
        /// <para>
        /// ⚠️ <b>Avuç noktasını hesaplayan tek yer burasıdır</b> ve avuca bakan her tüketici
        /// (kanonik kavrama, soket mesafesi, çerçeve mesafesi, ön kabza kapısı) buradan geçer:
        /// ikinci bir yol açılırsa ofset iki yerde yaşar ve biri güncellenip öteki unutulur.
        /// </para>
        /// </summary>
        public static bool TryResolvePalm(OVRInput.Controller hand, out Pose palm)
        {
            Transform anchor = ResolveHandAnchor(hand);
            if (anchor == null)
            {
                palm = default;
                return false;
            }

            palm = HandGripPivot.Resolve(anchor, HandGripPivot.IsRight(hand));
            return true;
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
