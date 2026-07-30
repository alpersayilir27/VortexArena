using System.Collections.Generic;
using Oculus.Interaction;
using UnityEngine;
using UnityEngine.SceneManagement;
using VortexArena.Core.Arena;

namespace VortexArena.Core.Combat
{
    /// <summary>
    /// <c>weaponSource:"random"</c> modlarının (§10.5) silah kaynağı: sahnedeki <b>rafı kaldırır</b>
    /// ve oyuncuya <b>grip'e basılı tuttukça</b> rastgele silah verir.
    /// <para>
    /// İki iş yapar, ikisi de yalnız kural <see cref="ModeWeaponSource.RandomGrant"/> iken:
    /// <list type="number">
    /// <item>Sahne süpürmesi: raf silahları ve taban bölgeleri gizlenir (bu modda "tabanına dön"
    /// diye bir şey yok — canlanma sabit durmakla oluyor).</item>
    /// <item>Verme döngüsü: her elde grip basılıyken o elde rastgele bir silah durur; bırakılınca
    /// yok olur, tekrar basınca YENİSİ gelir.</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Neden kendini önyükleyen tekil</b> (<see cref="PlayerCombatState"/> deseni): sahneye
    /// bileşen konsaydı her yeni arenaya elle bir kurulum adımı doğardı ve CLAUDE.md'deki arena
    /// listesi büyürdü. Bu bileşen görünmezdir; TDM'de (ve kural gelmemişken) hiçbir şey yapmaz.
    /// </para>
    /// <para>
    /// <b>Admin gözlemcide verme yolu kendiliğinden kapalıdır:</b> <c>AdminSpectator</c> BB
    /// rig'i kapattığı için <see cref="OVRCameraRig"/> aranınca bulunamaz (arama pasif objeleri
    /// dahil etmez) → el anchor'ı yok → silah verilmez. Süpürme ise role bakmaz: raf FFA'da
    /// gözlemcinin ekranında da durmamalı.
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

        /// <summary>Sol/sağ elde duran verilen silah örneği (yoksa null).</summary>
        private Weapon _grantedLeft;
        private Weapon _grantedRight;

        private OVRCameraRig _rig;
        private float _nextRigScanAt;

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
            Instance = null;
        }

        private void Update()
        {
            if (Instance != this || !IsRandomGrant)
            {
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
            ApplyRules();
        }

        /// <summary>Kural değişiminin tek uygulama noktası: RandomGrant'e girerken süpür, çıkarken
        /// geri al ve eldekileri temizle.</summary>
        private void ApplyRules()
        {
            if (IsRandomGrant)
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

        /// <summary>Loadout'tan rastgele bir silahı el anchor'ının altına örnekler.
        /// <para>Ard arda aynı silahın gelmesi ENGELLENMEZ: iki silahlık bir havuzda "asla aynısı
        /// gelmesin" kuralı rastgeleliği sırayla-dağıtıma çevirirdi.</para></summary>
        private Weapon Grant(OVRInput.Controller hand, Transform anchor)
        {
            WeaponDefinition definition = PickRandom();
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
            weapon.GrantTo(hand);
            return weapon;
        }

        /// <summary>
        /// Verilen silah elde SABİT durur: kavrama ve fizik yolları kapatılır.
        /// <para>
        /// Kapatılmasaydı silah (a) diğer elle ya da uzaktan kavranabilir, (b) yer çekimiyle elden
        /// düşer, (c) çarpışmalarıyla oyuncunun kendi ışınına takılırdı.
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

        private WeaponDefinition PickRandom()
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

            return _pool.Count == 0 ? null : _pool[Random.Range(0, _pool.Count)];
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
