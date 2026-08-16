using System;
using System.Collections.Generic;
using Oculus.Interaction.HandGrab.Visuals;
using Oculus.Interaction.Input;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VortexArena.Core.Combat;
using VortexArena.Core.Player;

namespace VortexArena.Core.Editor
{
    /// <summary>
    /// <c>Tools &gt; VortexArena &gt; Weapons &gt; Kavrama Pozu Stüdyosu</c> — silahın elde nasıl
    /// duracağını <b>gözlük takmadan</b> ayarlama tezgâhı.
    /// <para>
    /// <b>Akış:</b> <c>WPN_*</c> prefabını PREFAB KİPİNDE aç (çift tık) → stüdyo penceresi stage'i
    /// kendiliğinden tanır → <b>Elleri Oluştur</b> → hayalet eller sahnede belirir → elleri
    /// kabzalara sürükle/çevir, parmak duruşunu elin Inspector'ından preset olarak seç → istersen
    /// <b>Karşı Ele Aynala</b> → <b>Kaydet</b>.
    /// </para>
    /// <para>
    /// ⚠️ <b>Kullanıcının sürüklediği kök KUMANDA (anchor) çerçevesidir</b> — <c>[VA El_*]</c> kökü
    /// <c>OVRCameraRig.left/rightHandAnchor</c>'ın silah üstündeki yerini temsil eder ve kayıt bu kökün
    /// eşyaya göre pozudur (<see cref="ItemGripPose"/>: anchor uzayı, telle aynı). Kökün +Z'si
    /// kumandanın ilerisidir: kök silahla hizalıyken (kimlik) silah oyunda kumandayla hizalı gelir,
    /// yani <b>kökü döndürmeden yalnız taşımak silahın yönüne dokunmaz</b>. Kökün altında iki
    /// KİLİTLİ görsel çocuk durur: <b>Quest 3 kumanda modeli</b> (kimlik pozda — oyunda anchor'ın altında
    /// tam böyle durur, silah ONA göre hizalanır) ve <b>ISDK hayalet eli</b> (köke göre anchor→bilek
    /// pozunda: ölçülmüş sabit <see cref="HandGripConvention.AnchorToWrist"/> varsa o, yoksa hayaletin
    /// kendi iskeletinden tahmin — <see cref="ResolveGhostOffset"/>). Hayalet KAYDA GİRMEZ; tahminde
    /// yalnız elin görseli gerçek elden biraz sapar. Kayıt bilek uzayında tutulsaydı "eli hiç
    /// döndürmedim" bile anchor→bilek deltası kadar (onlarca derece) dönük bir silah üretirdi.
    /// </para>
    /// <para>
    /// ⚠️ <b>Ana kabzada tezgâhtaki kök SİLAHI DÖNDÜRÜR</b>: runtime ana eli
    /// <c>item = anchor ∘ Inverse(kayıt)</c> ile çözüyor, yani "silah ele göre durur, el silaha göre
    /// değil". Burada kökü kabzada çevirmek oyunda namlunun baktığı yönü değiştirir. ÖN kabzada ise
    /// tersi geçerlidir: ikinci elin kumandası <c>item ∘ kayıt</c>'a, sentetik bilek onun deltası
    /// kadar ötesine kilitlenir, yani el silaha yapışır.
    /// </para>
    /// <para>
    /// ⚠️ <b>Eller prefabın İÇİNE girmez</b>: her el, prefab stage sahnesinin ayrı bir KÖK objesidir
    /// (<see cref="HideFlags.DontSave"/>). Prefab kipinde diske yalnız <c>prefabContentsRoot</c>
    /// altındaki ağaç yazılır — düzenleme ortamının kendi objeleri de tam olarak böyle durur. El
    /// prefabın altına asılsaydı ilk kaydetmede silahın içine bir el modeli girer ve arenada havada
    /// el olarak görünürdü.
    /// </para>
    /// <para>
    /// ⚠️ <b>Prefab içeriğine HİÇBİR ŞEY yazılmaz</b> (poz düğümü, el rig'i, işaretçi): kaydın tek
    /// yeri <c>WD_*.asset</c>'tir. Prefabta duran ikinci bir tarif, "hangisi geçerli" sorusunu
    /// prefabı her açanın kafasında yeniden doğururdu.
    /// </para>
    /// <para>
    /// <b>Aracın var olma sebebi geri besleme süresidir:</b> kavrama tek doğru değeri olan bir sayı
    /// değil, "avuç kabzaya değiyor mu / işaret parmağı tetiğe ulaşıyor mu" sorusunun cevabıdır. O
    /// soruyu APK build'i + gözlük turuyla sormak her denemeyi dakikalara çıkarıyordu.
    /// </para>
    /// <para>
    /// ⚠️ <b>Dialog YOK</b> (<c>WeaponKitBuilder</c> ile aynı gerekçe): modal dialog Unity ana
    /// thread'ini kilitler ve CLI/pipeline üzerinden çalıştırıldığında komut timeout verir. Sonuç
    /// <see cref="Debug.Log"/> ile bildirilir.
    /// </para>
    /// </summary>
    internal sealed class GripPoseStudio : EditorWindow
    {
        internal const string LOG = "[GripPoseStudio]";

        /// <summary>
        /// El köklerinin ad öneki. ⚠️ Ad <b>anahtardır</b>: eller pencerede değil SAHNEDE yaşıyor
        /// (domain reload pencereyi de alanları da sıfırlayabilir) ve her seferinde adlarıyla geri
        /// bulunuyorlar. Köşeli parantez, kullanıcının kendi objeleriyle karışmasını engeller.
        /// </summary>
        internal const string HAND_ROOT_PREFIX = "[VA El_";

        /// <summary>
        /// ISDK'nın el modeli sağlayıcısı — <b>OpenXR</b> iskeleti için.
        /// <para>⚠️ <b>Yol sabit tutulur, ISDK'nın <c>HandGhostProviderUtils</c>'ü KULLANILMAZ:</b>
        /// o sınıf <c>Oculus.Interaction.Editor</c> asmdef'indedir ve ona referans vermek bu aracı
        /// ISDK'nın editör derlemesine bağlardı (paket yükseltmelerinde kırılan, çekirdeğin hiç
        /// ihtiyaç duymadığı bir bağ).</para>
        /// </summary>
        private const string GHOST_PROVIDER_PATH_OPENXR =
            "Packages/com.meta.xr.sdk.interaction/Runtime/Prefabs/HandGrab/OpenXRGhostProvider.asset";

        /// <summary>Aynı sağlayıcının <b>OVR</b> iskeleti karşılığı.</summary>
        private const string GHOST_PROVIDER_PATH_OVR =
            "Packages/com.meta.xr.sdk.interaction/Runtime/Prefabs/HandGrab/GhostProvider.asset";

        /// <summary>
        /// Kumanda kökünün altına konan KUMANDA MODELİNİN kaynağı: Meta core SDK'nın kumanda prefabı ve
        /// içindeki Quest 3 (Touch Plus) modelleri. Model, oyunda anchor'ın altında kimlik pozda durduğu
        /// için (<c>OVRControllerHelper</c> hiçbir ofset uygulamaz) tezgâhta da köke kimlikle konur —
        /// yani gördüğün kumanda tam olarak oyunda izlenen kumandadır: silah ona göre hizalanır.
        /// </summary>
        private const string CONTROLLER_PREFAB_PATH = "Packages/com.meta.xr.sdk.core/Prefabs/OVRControllerPrefab.prefab";
        private const string CONTROLLER_MODEL_RIGHT = "MetaQuestTouchPlus_Right";
        private const string CONTROLLER_MODEL_LEFT = "MetaQuestTouchPlus_Left";

        /// <summary>Kökün altındaki iki görsel çocuğun adları (kimlik: yeniden bulunmak için).</summary>
        private const string GHOST_NAME = "Hand";
        private const string CONTROLLER_NAME = "Controller";

        /// <summary>Kumanda modeli bulunamadığında uyarı bir kez basılsın diye.</summary>
        private static bool _controllerModelWarned;

        /// <summary>
        /// Kabza düğümünün ad anahtarları (büyük/küçük harf duyarsız, <b>sırayla</b> denenir).
        /// <para>⚠️ Sıra anlamlıdır: daha SPESİFİK anahtar önce gelir, yoksa geniş olan
        /// (<c>guard</c>) yanlış parçayı kapar. Aynı sebeple ön kabza listesinde <c>guard</c>
        /// en sonda durur.</para>
        /// </summary>
        private static readonly string[] GRIP_KEYS = { "pistolgrip", "grip", "handle" };

        /// <summary>Ön kabza (iki elli tutuşta öndeki el) düğümünün ad anahtarları.</summary>
        private static readonly string[] FOREGRIP_KEYS =
            { "handguard", "barrelguard", "foregrip", "forend", "guard" };

        /// <summary>Stage kapalıyken hedef seçimi (yalnız "prefabı aç" için kullanılır).</summary>
        [SerializeField] private GameObject _prefab;

        /// <summary>
        /// Bir önceki karede tezgâhta YAŞAYAN el var mıydı — "eller kalktı" bildirimi için.
        /// <para>⚠️ Serialize EDİLMEZ: bayrak yalnız iki kare arasındaki farkı anlatıyor, kalıcı
        /// olsaydı domain reload sonrası (eller zaten <c>DontSave</c>, o geçişte gidiyorlar) her
        /// açılışta sebepsiz bir uyarı basardı.</para>
        /// </summary>
        [NonSerialized] private bool _handsSeen;

        /// <summary>Eller pencere dışında bir sebeple kalktı — bildirim yeniden kurulana dek durur.</summary>
        [NonSerialized] private bool _handsLost;

        private static HandGhostProvider _ghostProvider;

        /// <summary>Yüklü sağlayıcının yolu — teşhis satırında gösterilir.</summary>
        private static string _ghostProviderPath;

        /// <summary>
        /// Sağlayıcı hiç bulunamadığında uyarı bir kez basılsın diye.
        /// <para>⚠️ Bu yol <c>OnGUI</c>'den her karede geçebiliyor: bayraksız hâlde eksik bir asset
        /// konsolu saniyede onlarca satırla boğardı.</para>
        /// </summary>
        private static bool _ghostProviderWarned;

        // --------------------------------------------------------------------------- pencere

        // Weapons menüsündeki TEK öğe: silah kiti ve net eşya kataloğu Configure All Build
        // Elements'in eşitlemesinde koşuyor, ayrı düğmeleri yok. Stüdyo bir yazım penceresidir —
        // kavrama insan gözüyle yazılır, o yüzden menüde kalır.
        [MenuItem("Tools/VortexArena/Weapons/Kavrama Pozu Stüdyosu", false, 20)]
        private static void Open()
        {
            GripPoseStudio window = GetWindow<GripPoseStudio>();
            window.titleContent = new GUIContent("Kavrama Tezgâhı");
            window.minSize = new Vector2(340f, 360f);
            window.Show();
        }

        /// <summary>
        /// Temizlik kancaları <b>pencereden bağımsız</b> kurulur.
        /// <para>⚠️ Pencerenin <c>OnEnable</c>'ına bağlanamazlar: kullanıcı pencereyi kapatıp prefab
        /// kipinde çalışmaya devam edebiliyor ve o hâlde eller ortada kalır. Eller
        /// <see cref="HideFlags.DontSave"/> olduğu için diske yazılmazlar ama Play'e girildiğinde
        /// çalışan oyunun ortasında iki el modeli olarak dururlardı.</para>
        /// </summary>
        [InitializeOnLoadMethod]
        private static void InstallCleanupHooks()
        {
            PrefabStage.prefabStageClosing += stage => DestroyHands(stage.scene);
            // Scene View'da hayalet elin mesh'ine tıklamak ÇOCUĞU seçer; sürüklenecek şey ise
            // kumanda kökü. Seçim köke yönlendirilir ki tutamaçlar kökte belirsin — çocuğu taşımak
            // kaydı DEĞİŞTİRMEZ (kayıt kökten okunur) ve kullanıcı "kaydettim ama değişmedi" yaşardı.
            Selection.selectionChanged += RedirectSelectionToHandRoot;
            EditorApplication.playModeStateChanged += change =>
            {
                if (change == PlayModeStateChange.ExitingEditMode)
                {
                    DestroyAllHands();
                }
            };
            // ⚠️ Unity, prefab kipini KAYDEDERKEN (Auto Save dahil) önizleme sahnesindeki HER
            // fazladan kökü HideAndDontSave'e çevirir — başlangıç bayrağı ne olursa olsun
            // (ölçüldü: DontSave de None da çevriliyor). HideInHierarchy eli hiyerarşiden düşürür
            // (obje yaşamaya ve çizilmeye devam eder — belirtisi "el hiyerarşiden siliniyor ama
            // görseli kalıyor"), NotEditable da üstüne kilit vurur. Çevirme kayıt akışının BİRDEN
            // ÇOK noktasında koşuyor (prefabSaved anında çevrilmiş oluyor, olay içinde geri yazılan
            // bayrak kayıt bittikten sonra YİNE çevrilebiliyor) ve delayCall editör odaksızken hiç
            // işlemiyor — bu yüzden tek atımlık bir olay kancası yetmez, bekçi update'te durur:
            // her tikte (yalnız gerektiğinde yazarak) elleri hiyerarşiye geri getirir.
            EditorApplication.update += RestoreHandFlags;
            // ⚠️ Temizlik YALNIZ açılan sahneye uygulanır (DestroyAllHands DEĞİL): açık stage'i de
            // süpüren bir temizlik, stage içeriği yeniden yüklenirken elleri BİZİM silmemiz
            // demekti. Açık stage'in kendi sahnesine burada hiç dokunulmaz; onun temizliği
            // prefabStageClosing'de.
            EditorSceneManager.sceneOpened += (scene, mode) =>
            {
                // ⚠️ Önizleme sahnesi elenirken stage referansına GÜVENİLMEZ (içerik yeniden
                // yüklenirken stage'in scene alanı bir an bayat olabiliyor) ve "path'i boştur"
                // varsayımına da güvenilmez: önizleme sahnesinin path'i PREFABIN ASSET YOLUDUR
                // (ölçüldü: 'Assets/.../WPN_*.prefab'). Gerçekten açılan bir sahne her zaman bir
                // .unity dosyasıdır — ayırt edici özellik budur.
                PrefabStage stage = CurrentStage();
                bool previewScene = string.IsNullOrEmpty(scene.path) ||
                                    scene.path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase) ||
                                    (stage != null && stage.scene == scene);
                if (previewScene)
                {
                    return;
                }

                DestroyHands(scene);
            };
        }

        /// <summary>
        /// Seçim bir elin ALT objesine (hayalet mesh, eklem) düşerse kumanda köküne taşır — kök,
        /// kaydedilen tek transformdur (gerekçe <see cref="InstallCleanupHooks"/>'ta).
        /// </summary>
        private static void RedirectSelectionToHandRoot()
        {
            GameObject active = Selection.activeGameObject;
            if (active == null || active.GetComponent<GripHandAuthoring>() != null)
            {
                return;
            }

            var owner = active.GetComponentInParent<GripHandAuthoring>();
            if (owner != null)
            {
                Selection.activeGameObject = owner.gameObject;
            }
        }

        /// <summary>
        /// Unity'nin prefab kaydının gizlediği elleri hiyerarşiye geri getirir (bkz.
        /// <see cref="InstallCleanupHooks"/> içindeki gerekçe). Her editör tikinde koşar; bu
        /// yüzden ucuz olmak zorundadır — bayrağı bozulmamış ele HİÇ yazmaz, hiyerarşiyi yalnız
        /// gerçekten bir şey düzelttiğinde tazeler.
        /// </summary>
        private static void RestoreHandFlags()
        {
            PrefabStage stage = CurrentStage();
            if (stage == null)
            {
                return;
            }

            List<GripHandAuthoring> hands = FindHands(stage.scene);
            bool restored = false;
            for (int i = 0; i < hands.Count; i++)
            {
                if (hands[i] == null)
                {
                    continue;
                }

                GameObject go = hands[i].gameObject;
                if ((go.hideFlags & (HideFlags.HideInHierarchy | HideFlags.NotEditable)) == 0)
                {
                    continue;
                }

                MarkDontSave(go);
                HideIsdkComponents(GhostOf(hands[i]));
                // Çevirme bileşen bayraklarına da bulaşırsa kullanıcıya kalan iki bileşen
                // (Transform + preset) Inspector'da kilitli/gizli kalmasın.
                hands[i].hideFlags &= ~(HideFlags.HideInInspector | HideFlags.NotEditable);
                hands[i].transform.hideFlags &= ~(HideFlags.HideInInspector | HideFlags.NotEditable);
                restored = true;
            }

            if (restored)
            {
                EditorApplication.RepaintHierarchyWindow();
            }
        }

        /// <summary>
        /// Proje penceresinde bir <c>WPN_*</c> prefabı seçilince hedef kendiliğinden dolar (prefab
        /// kipini açan düğme için).
        /// </summary>
        private void OnSelectionChange()
        {
            GameObject candidate = Selection.activeGameObject;
            if (candidate != null &&
                PrefabUtility.IsPartOfPrefabAsset(candidate) &&
                candidate.GetComponent<Weapon>() != null)
            {
                _prefab = candidate;
            }

            Repaint();
        }

        // ------------------------------------------------------------------------------ GUI

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Kavrama Tezgâhı", EditorStyles.boldLabel);

            if (EditorApplication.isPlaying)
            {
                EditorGUILayout.HelpBox(
                    "Play kipinde kavrama yazılmaz — kayıt diske AssetDatabase ile iniyor ve Play " +
                    "oturumunda yazılan her değer bir sonraki domain reload'da belirsizleşir.",
                    MessageType.Info);
                return;
            }

            PrefabStage stage = CurrentStage();
            Transform weaponRoot = StageWeaponRoot(stage);

            if (weaponRoot == null)
            {
                DrawNoStageGui(stage);
                return;
            }

            DrawStageGui(stage, weaponRoot);
        }

        /// <summary>
        /// Prefab kipi kapalıyken tek iş vardır: hedefi prefab kipinde açmak.
        /// <para>⚠️ Sahnede tezgâh kuran eski yol GERİ GELMEZ: elin yerleşimi silaha göre ölçülüyor
        /// ve prefab kipi bu referansı (prefab kökü) zaten kendi başına, silahın kendi ölçeğiyle
        /// veriyor. Sahnede ikinci bir silah kopyası tutmak, kaydın hangi örneğe göre alındığını
        /// belirsizleştirirdi.</para>
        /// </summary>
        private void DrawNoStageGui(PrefabStage stage)
        {
            _prefab = (GameObject)EditorGUILayout.ObjectField(
                "Silah prefabı", _prefab, typeof(GameObject), false);

            bool usable = _prefab != null && ResolveDefinition(_prefab) != null;

            using (new EditorGUI.DisabledScope(!usable))
            {
                if (GUILayout.Button("Prefabı Prefab Kipinde Aç", GUILayout.Height(26f)))
                {
                    AssetDatabase.OpenAsset(_prefab);
                }
            }

            if (stage != null)
            {
                EditorGUILayout.HelpBox(
                    "Açık prefab kipinde Weapon bileşeni yok — bu bir silah prefabı değil.",
                    MessageType.Warning);
            }
            else if (_prefab == null)
            {
                EditorGUILayout.HelpBox(
                    "Hedef yok: proje penceresinden bir WPN_* prefabı seç (ya da yukarıdaki alana " +
                    "sürükle), sonra prefab kipinde aç.",
                    MessageType.Info);
            }
            else if (!usable)
            {
                EditorGUILayout.HelpBox(
                    "Bu prefabın Weapon bileşeni ya da tanımı (WeaponDefinition) yok — " +
                    "kaydedilecek asset bulunamaz.",
                    MessageType.Warning);
            }

            EditorGUILayout.HelpBox(
                "Akış: prefabı prefab kipinde aç → Elleri Oluştur → kumanda köklerini kabzalara oturt " +
                "(hayalet el köke bağlı çizilir), parmak duruşunu preset'ten seç → (istersen) Aynala → " +
                "Kaydet.",
                MessageType.None);

            DrawGhostSourceSection();
        }

        private void DrawStageGui(PrefabStage stage, Transform weaponRoot)
        {
            WeaponDefinition definition = ResolveDefinition(weaponRoot.gameObject);

            EditorGUILayout.LabelField("Hedef", weaponRoot.name);

            if (definition == null)
            {
                EditorGUILayout.HelpBox(
                    "Prefabın Weapon bileşeninde tanım (WeaponDefinition) yok — kavrama alanları " +
                    "yazılamaz.",
                    MessageType.Warning);
                return;
            }

            List<GripHandAuthoring> hands = FindHands(stage.scene);
            int live = CountLive(hands);

            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Ana Kabza Ellerini Oluştur", GUILayout.Height(24f)))
                {
                    CreateHandPair(stage, weaponRoot, definition, GripSocketKind.Primary);
                }

                using (new EditorGUI.DisabledScope(!definition.IsTwoHanded))
                {
                    if (GUILayout.Button("Ön Kabza Ellerini Oluştur", GUILayout.Height(24f)))
                    {
                        CreateHandPair(stage, weaponRoot, definition, GripSocketKind.Secondary);
                    }
                }
            }

            DrawHandList(hands);

            // ⚠️ Durum YALNIZ Layout olayında güncellenir: OnGUI kare başına en az iki kez koşuyor
            // (Layout + Repaint) ve iki geçişte FARKLI sayıda kontrol çizmek Unity'nin layout
            // eşleştirmesini bozar ("control position in a group with only N controls"). Bayrak
            // sabit kaldığı için kutu iki geçişte de aynı çizilir.
            if (Event.current.type == EventType.Layout)
            {
                if (live > 0)
                {
                    _handsSeen = true;
                    _handsLost = false;
                }
                else if (_handsSeen)
                {
                    _handsSeen = false;
                    _handsLost = true;
                }
            }

            if (_handsLost)
            {
                // Son çare bildirimi: eller tezgâhta duruyordu ve pencere dışında bir sebeple
                // (stage'in içeriğini yeniden yükleyen bir kayıt) yok oldular. Sessiz kalmak
                // "araç elleri kaybetti" izlenimi verirdi; yapılacak iş tek düğmedir ve kavraması
                // yazılmış silahta el kayıttan aynı yere geri gelir.
                EditorGUILayout.HelpBox(
                    "Eller tezgâhtan kalktı (prefab kipi içeriği yeniden yüklenmiş olabilir). " +
                    "Yeniden oluştur: kavraması yazılmış bir silahta eller kayıttan aynı yere gelir.",
                    MessageType.Info);
            }

            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(live == 0))
                {
                    if (GUILayout.Button("Kaydet", GUILayout.Height(26f)))
                    {
                        SaveHands(weaponRoot, definition, hands);
                    }

                    if (GUILayout.Button("Elleri Temizle", GUILayout.Height(26f)))
                    {
                        DestroyHands(stage.scene);
                        Debug.Log($"{LOG} Eller silindi (kaydedilmemiş değişiklikler atıldı).");
                    }
                }
            }

            EditorGUILayout.Space();
            DrawLiveValues(weaponRoot, definition, hands);

            DrawGhostSourceSection();

            EditorGUILayout.HelpBox(
                "Yazan tek düğme Kaydet'tir. Sürüklediğin kök KUMANDADIR — altındaki kumanda modeli " +
                "oyunda izlenen kumandanın ta kendisidir, silahı ONA göre hizala (mavi ok = kumandanın " +
                "ilerisi). Kökü yalnız TAŞIRSAN silah oyunda kumandayla hizalı gelir, kökü ÇEVİRİRSEN ana " +
                "elde SİLAH çevrilir; ön kabzada el silaha yapışır. Hayalet el ve kumanda modeli " +
                "kilitlidir (taşınmaz) — kayıt kökten okunur. Parmaklar preset'ten gelir.",
                MessageType.None);

            if (AnyGhostEstimated(hands))
            {
                EditorGUILayout.HelpBox(
                    "Hayalet el TAHMİNLE çizildi (anchor→bilek sabiti ölçülmemiş: " +
                    "HandGripConvention.*AnchorToWrist = kimlik). Elin kumandaya göre duruşu yaklaşıktır, " +
                    "kumanda modeli ise kesindir — hizayı ona göre yap. Kayıt bundan etkilenmez. Tam el " +
                    "için HandGripPoser'ın başlıkta bastığı iki satırı (editör Play'i ya da APK'da " +
                    "adb logcat -s Unity) sabite yapıştır.",
                    MessageType.Info);
            }
        }

        /// <summary>Tezgâhta hayalet ofseti tahminle kurulmuş (ölçülmemiş) yaşayan el var mı.</summary>
        private static bool AnyGhostEstimated(List<GripHandAuthoring> hands)
        {
            for (int i = 0; i < hands.Count; i++)
            {
                if (hands[i] != null && !hands[i].GhostOffsetMeasured)
                {
                    return true;
                }
            }

            return false;
        }

        private void DrawHandList(List<GripHandAuthoring> hands)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Tezgâhtaki eller", EditorStyles.boldLabel);

            if (CountLive(hands) == 0)
            {
                EditorGUILayout.LabelField("(yok — yukarıdan oluştur)", EditorStyles.miniLabel);
                return;
            }

            for (int i = 0; i < hands.Count; i++)
            {
                GripHandAuthoring hand = hands[i];
                if (hand == null)
                {
                    continue;
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(
                        $"{hand.Kind} · {(hand.RightHand ? "sağ" : "sol")}",
                        GUILayout.Width(140f));

                    if (GUILayout.Button("Seç"))
                    {
                        Selection.activeGameObject = hand.gameObject;
                        SceneView.lastActiveSceneView?.FrameSelected();
                    }
                }
            }
        }

        /// <summary>
        /// Kaydedilecek sayıları <b>canlı</b> gösterir, yanında kaydın diskteki durumunu.
        /// <para>⚠️ Salt okunurdur ve öyle kalır: bu sayıların düzenlenebilir olması, aynı kavramayı
        /// iki yerde (bir alanda ve bir transformda) tarif etmek olurdu — ikisi zamanla sessizce
        /// sapar ve belirtisi "silah bazı yerlerde doğru duruyor" olurdu.</para>
        /// </summary>
        private void DrawLiveValues(Transform weaponRoot, WeaponDefinition definition,
            List<GripHandAuthoring> hands)
        {
            if (CountLive(hands) == 0)
            {
                return;
            }

            EditorGUILayout.LabelField("Kaydedilecek (salt okunur)", EditorStyles.boldLabel);

            for (int i = 0; i < hands.Count; i++)
            {
                GripHandAuthoring hand = hands[i];
                if (hand == null)
                {
                    // ⚠️ Liste bu karenin BAŞINDA toplandı; el o andan sonra yok edilmiş olabilir
                    // (stage'i dışarıdan yenileyen her yol bunu yapar). Ölü girdiyi çizmeye
                    // kalkmak MissingReferenceException'dır ve pencereyi tümden karartır.
                    continue;
                }

                Pose local = AnchorInItem(weaponRoot, hand.transform);
                string state = definition.HasGrip(hand.Kind, hand.RightHand)
                    ? "yazılmış"
                    : "yazılmamış";

                EditorGUILayout.LabelField(
                    $"{hand.Kind} · {(hand.RightHand ? "sağ" : "sol")}",
                    $"{Format(local.position)}  {Format(local.rotation.eulerAngles)}  " +
                    $"{HandGripPresets.Label(hand.Preset)}  ({state})");
            }
        }

        private static string Format(Vector3 value)
        {
            return $"({value.x:0.####}, {value.y:0.####}, {value.z:0.####})";
        }

        // ------------------------------------------------------------------------- stage/el

        private static PrefabStage CurrentStage()
        {
            return PrefabStageUtility.GetCurrentPrefabStage();
        }

        /// <summary>Açık prefab kipindeki silahın kökü; prefab bir silah değilse <c>null</c>.</summary>
        private static Transform StageWeaponRoot(PrefabStage stage)
        {
            GameObject root = stage != null ? stage.prefabContentsRoot : null;
            if (root == null || root.GetComponent<Weapon>() == null)
            {
                return null;
            }

            return root.transform;
        }

        internal static string HandRootName(GripSocketKind kind, bool rightHand)
        {
            return $"{HAND_ROOT_PREFIX}{kind}_{(rightHand ? "R" : "L")}]";
        }

        private static List<GripHandAuthoring> FindHands(Scene scene)
        {
            var found = new List<GripHandAuthoring>();
            if (!scene.IsValid() || !scene.isLoaded)
            {
                return found;
            }

            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                if (roots[i] == null || !roots[i].name.StartsWith(HAND_ROOT_PREFIX))
                {
                    continue;
                }

                var authoring = roots[i].GetComponent<GripHandAuthoring>();
                if (authoring != null)
                {
                    found.Add(authoring);
                }
            }

            return found;
        }

        /// <summary>
        /// Listedeki <b>yaşayan</b> el sayısı.
        /// <para>⚠️ <c>Count</c> yetmez: liste bir karenin başında toplanıyor ve içindeki objeler o
        /// kare bitmeden yok edilebiliyor (prefab kipini dışarıdan yenileyen her yol). Ölü girdiyi
        /// saymak "Kaydet" düğmesini boş bir listeye açık bırakırdı.</para>
        /// </summary>
        private static int CountLive(List<GripHandAuthoring> hands)
        {
            int live = 0;
            for (int i = 0; i < hands.Count; i++)
            {
                if (hands[i] != null)
                {
                    live++;
                }
            }

            return live;
        }

        private static GripHandAuthoring FindHand(List<GripHandAuthoring> hands, GripSocketKind kind,
            bool rightHand)
        {
            for (int i = 0; i < hands.Count; i++)
            {
                if (hands[i] != null && hands[i].Kind == kind && hands[i].RightHand == rightHand)
                {
                    return hands[i];
                }
            }

            return null;
        }

        private static void DestroyHands(Scene scene)
        {
            List<GripHandAuthoring> hands = FindHands(scene);
            for (int i = 0; i < hands.Count; i++)
            {
                if (hands[i] != null)
                {
                    DestroyImmediate(hands[i].gameObject);
                }
            }

            SceneView.RepaintAll();
        }

        /// <summary>Açık her sahnedeki (prefab kipi dahil) elleri siler.</summary>
        private static void DestroyAllHands()
        {
            PrefabStage stage = CurrentStage();
            if (stage != null)
            {
                DestroyHands(stage.scene);
            }

            for (int s = 0; s < SceneManager.sceneCount; s++)
            {
                DestroyHands(SceneManager.GetSceneAt(s));
            }
        }

        // ------------------------------------------------------------------------ el kurma

        private static void CreateHandPair(PrefabStage stage, Transform weaponRoot,
            WeaponDefinition definition, GripSocketKind kind)
        {
            // ⚠️ İki el de BAĞIMSIZ denenir: sağ el kurulamazsa sol elin de hiç kurulmaması,
            // kullanıcıya "sol el hiç eklenmiyor" gibi görünen ve asıl hatayı gizleyen bir belirti
            // üretir. Başarısız olan el kendi hata satırını zaten basıyor.
            GripHandAuthoring right = EnsureHand(stage, weaponRoot, definition, kind, true);
            GripHandAuthoring left = EnsureHand(stage, weaponRoot, definition, kind, false);

            GripHandAuthoring focus = right != null ? right : left;
            if (focus != null)
            {
                Selection.activeGameObject = focus.gameObject;
                SceneView.lastActiveSceneView?.FrameSelected();
            }

            NotifyHandsChanged();
        }

        /// <summary>
        /// Açık stüdyo pencerelerine "eller değişti" der.
        /// <para>⚠️ <c>GetWindow</c> KULLANILMAZ: o, pencere kapalıyken yenisini açar ve odağı
        /// çalar — el kurma/aynalama sahne penceresinden tetiklenebiliyor ve orada odağın kayması
        /// kullanıcının sürüklemesini bölerdi.</para>
        /// </summary>
        private static void NotifyHandsChanged()
        {
            GripPoseStudio[] windows = Resources.FindObjectsOfTypeAll<GripPoseStudio>();
            for (int i = 0; i < windows.Length; i++)
            {
                windows[i].Repaint();
            }
        }

        /// <summary>
        /// Bir kavrama noktasının elini kurar (varsa mevcudu döner).
        /// <para>
        /// ⚠️ <b>Kök KUMANDA çerçevesidir, hayalet el onun ÇOCUĞU:</b> kaydedilen şey kökün pozudur
        /// (<see cref="AnchorInItem"/>), ISDK hayalet eli köke <see cref="HandGripConvention.AnchorToWrist"/>
        /// sabitiyle bağlanır — kumandayı tutan elin bileği kumandaya göre nerede duruyorsa orada.
        /// Sürüklenen şey ile kaydedilen şey aynı transformdur; çocuk sürüklenirse seçim köke
        /// yönlendirilir (<see cref="RedirectSelectionToHandRoot"/>).
        /// </para>
        /// <para>
        /// ⚠️ Yerel ölçek 1'e sabitlenir: el prefabın altında DEĞİL, sahnenin kökündedir — silahın
        /// kendi ölçeği (<c>WPN_*</c> köklerinde 0.8) ele bulaşmaz. Bulaşsaydı "avuç kabzayı sarıyor
        /// mu" sorusu %25 yanlış bir orandan cevaplanırdı ve aracın tek işi o soruyu doğru
        /// cevaplatmak.
        /// </para>
        /// </summary>
        private static GripHandAuthoring EnsureHand(PrefabStage stage, Transform weaponRoot,
            WeaponDefinition definition, GripSocketKind kind, bool rightHand)
        {
            GripHandAuthoring existing = FindHand(FindHands(stage.scene), kind, rightHand);
            if (existing != null)
            {
                // Unity'nin kayıt yan etkisiyle gizlenmiş (HideAndDontSave) bir el burada
                // hiyerarşiye geri döner — "Elleri Oluştur" aynı zamanda kayıp elleri diriltir.
                // Görsel çocuklar (hayalet el, kumanda modeli) da yerlerine oturtulur: elle kaydırılmış
                // bir çocuk kayda girmez, ama kullanıcıyı yanıltır — buradan geri gelir.
                ApplyGhostOffset(existing);
                EnsureControllerModel(existing.gameObject, existing.RightHand);
                MarkDontSave(existing.gameObject);
                HideIsdkComponents(GhostOf(existing));
                return existing;
            }

            if (!TryGetGhostProvider(out HandGhostProvider provider))
            {
                return null;
            }

            Handedness handedness = rightHand ? Handedness.Right : Handedness.Left;
            HandGhost prototype = provider.GetHand(handedness);
            if (prototype == null)
            {
                Debug.LogWarning($"{LOG} El sağlayıcısında {handedness} el yok — el kurulamadı.");
                return null;
            }

            var root = new GameObject(HandRootName(kind, rightHand));
            SceneManager.MoveGameObjectToScene(root, stage.scene);
            root.transform.localScale = Vector3.one;

            var authoring = root.AddComponent<GripHandAuthoring>();
            if (authoring == null)
            {
                // ⚠️ Yarım el BIRAKILMAZ: bileşensiz kökü FindHands göremez (kimliği bileşende),
                // yani ne pencerede listelenir ne "Elleri Temizle" ile silinebilir — sahnede
                // kaydedilmemiş, sahipsiz bir obje olarak kalır ve ilk stage/Play geçişinde
                // sessizce kaybolur. Kurulamayan kökü hemen yok etmek o hayaleti hiç üretmez.
                DestroyImmediate(root);
                Debug.LogError($"{LOG} {kind}/{(rightHand ? "sağ" : "sol")} el kurulamadı: " +
                               "GripHandAuthoring eklenemedi. Sınıf RUNTIME asmdef'inde " +
                               "(VortexArena.Core, #if UNITY_EDITOR sarmalında) olmalı — Unity " +
                               "editör asmdef'inde derlenen bir MonoBehaviour'ı AddComponent ile " +
                               "kabul etmez ve null döner.");
                return null;
            }

            // Hayalet el: kökün çocuğu, köke göre anchor→bilek pozunda (ResolveGhostOffset: ölçülmüş
            // sabit, yoksa iskeletten tahmin). Kaydı hiç etkilemez, yalnız görseldir.
            HandGhost ghost = Instantiate(prototype, root.transform);
            GameObject handGo = ghost.gameObject;
            handGo.name = GHOST_NAME;
            handGo.transform.localScale = Vector3.one;

            HandPuppet puppet = ghost.GetComponent<HandPuppet>();

            // ⚠️ Ofset PRESET UYGULANMADAN ölçülür (bind pozu): tahmin başparmak kökünün yerini okuyor,
            // preset o kemiği kıvırınca ölçü o karenin duruşunu içerirdi.
            Pose ghostOffset = ResolveGhostOffset(puppet, handGo.transform, rightHand, out bool measured);
            authoring.SetGhostOffset(ghostOffset, measured);
            ApplyGhostOffset(authoring);
            EnsureControllerModel(root, rightHand);

            ItemGripPose recorded = definition.GetGrip(kind, rightHand);
            HandGripPreset preset = definition.HasGrip(kind, rightHand)
                ? recorded.preset
                : HandGripPresets.DefaultFor(kind);

            // Kök doğrudan yerleştirilir (puppet'ın SetRootPose'u DEĞİL: o hayalet elin kendi
            // transformunu yazar, oysa hayalet kökün altında ve yerel ofseti sabittir).
            // ⚠️ Parmak pozu ISDK'nın HandGhost.SetPose'u ile DEĞİL doğrudan puppet üzerinden verilir:
            // SetPose bir HandPose nesnesi ister ve o nesnenin taşıdığı eklem dizisi bizde ikinci
            // bir parmak kaynağı olurdu — parmakların tek kaynağı preset tablosudur.
            Pose start = ResolveStartPose(weaponRoot, definition, kind, rightHand);
            root.transform.SetPositionAndRotation(start.position, start.rotation);

            authoring.Resolve(puppet, kind, rightHand, preset);
            HideIsdkComponents(handGo);
            MarkDontSave(root);
            NotifyHandsChanged();
            return authoring;
        }

        /// <summary>Elin ISDK hayalet objesi (kökün çocuğu); yoksa <c>null</c>.</summary>
        private static GameObject GhostOf(GripHandAuthoring hand)
        {
            if (hand == null)
            {
                return null;
            }

            var ghost = hand.GetComponentInChildren<HandGhost>(true);
            return ghost != null ? ghost.gameObject : null;
        }

        /// <summary>
        /// Hayalet elin kumanda köküne göre yerel pozu (anchor→bilek).
        /// <para>
        /// <b>Ölçülmüş sabit varsa o</b> (<see cref="HandGripConvention.AnchorToWrist"/>, kimlik değilse):
        /// başlıkta <c>HandGripPoser</c>'ın logladığı değer, tek gerçek. <b>Yoksa TAHMİN</b> — el
        /// kökün tam üstünde ve aynı eksende çizilseydi ISDK bilek çerçevesi kumandayla aynı eksende
        /// olmadığı için "yan yatmış" bir el görünür, kullanıcı da onu düzeltmek için KÖKÜ çevirir ve
        /// oyunda silahı döndürürdü. Tahminin iki parçası: dönüş = anchor uzayındaki el anatomisi
        /// (<see cref="HandGripConvention.AnchorBasis"/> — uzak avatarın da kullandığı kabul) ⇐ hayaletin
        /// KENDİ iskeletinden ölçülen kemik bazı (<see cref="HandGripConvention.Correction"/>); konum =
        /// avuç merkezi kumandanın üstünde sayılır (<c>HandGripPivot</c>: avuç ≡ anchor) ve avuç merkezi
        /// OpenXR tanımıyla orta parmak metakarpının ortasıdır (bilek→orta parmak kökünün yarısı).
        /// </para>
        /// <para>⚠️ Tahmin KAYDA GİRMEZ: kayıt kökün pozudur, bu yalnız hayaletin nerede çizileceğidir.
        /// Sabit ölçülüp yapıştırılınca tahmin hiç okunmaz.</para>
        /// </summary>
        private static Pose ResolveGhostOffset(HandPuppet puppet, Transform ghostRoot, bool rightHand,
            out bool measured)
        {
            Pose constant = HandGripConvention.AnchorToWrist(rightHand);
            measured = !constant.Equals(Pose.identity);
            if (measured)
            {
                return constant;
            }

            if (puppet == null || ghostRoot == null ||
                !TryFindJoint(puppet, HandJointId.HandMiddle1, out Transform middleProximal) ||
                !TryFindJoint(puppet, HandJointId.HandThumb2, out Transform thumbProximal) ||
                !HandGripConvention.TryMeasureBoneBasis(ghostRoot, middleProximal, thumbProximal, rightHand,
                    out Quaternion boneBasis))
            {
                return Pose.identity;
            }

            Quaternion rotation = HandGripConvention.Correction(rightHand, boneBasis);
            Vector3 middleLocal = ghostRoot.InverseTransformPoint(middleProximal.position);
            Vector3 palmLocal = middleLocal * 0.5f;
            return new Pose(-(rotation * palmLocal), rotation);
        }

        private static bool TryFindJoint(HandPuppet puppet, HandJointId id, out Transform joint)
        {
            joint = null;
            List<HandJointMap> maps = puppet != null ? puppet.JointMaps : null;
            for (int i = 0; maps != null && i < maps.Count; i++)
            {
                if (maps[i] != null && maps[i].id == id && maps[i].transform != null)
                {
                    joint = maps[i].transform;
                    return true;
                }
            }

            return false;
        }

        /// <summary>Hayalet eli kökün altında kaydedilmiş ofsetine oturtur (elle kaydırılmışsa geri getirir).</summary>
        private static void ApplyGhostOffset(GripHandAuthoring hand)
        {
            GameObject ghost = GhostOf(hand);
            if (ghost == null)
            {
                return;
            }

            Pose offset = hand.GhostOffset;
            ghost.transform.localPosition = offset.position;
            ghost.transform.localRotation = offset.rotation;
            ghost.transform.localScale = Vector3.one;
        }

        /// <summary>
        /// Kökün altına Quest 3 kumanda modelini koyar (yoksa) — kimlik pozda: oyunda anchor'ın altında
        /// tam böyle durur, yani silahı hizalarken bakılacak GERÇEK referans budur (hayalet el ölçülmemiş
        /// sabitte tahminle çizilir, kumanda modeli değil).
        /// <para>Modelin <c>Animator</c>'ı sökülür (tuş animasyonu tezgâhta anlamsız); mesh ve ölçek
        /// prefabtakiyle aynı kalır. Bulunamazsa bir kez uyarır — el yine kurulur.</para>
        /// </summary>
        private static void EnsureControllerModel(GameObject root, bool rightHand)
        {
            if (root == null || root.transform.Find(CONTROLLER_NAME) != null)
            {
                return;
            }

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CONTROLLER_PREFAB_PATH);
            Transform source = prefab != null
                ? prefab.transform.Find(rightHand ? CONTROLLER_MODEL_RIGHT : CONTROLLER_MODEL_LEFT)
                : null;
            if (source == null)
            {
                if (!_controllerModelWarned)
                {
                    _controllerModelWarned = true;
                    Debug.LogWarning($"{LOG} Kumanda modeli bulunamadı ('{CONTROLLER_PREFAB_PATH}' → " +
                                     $"{CONTROLLER_MODEL_RIGHT}/{CONTROLLER_MODEL_LEFT}); kök yalnız gizmo ve " +
                                     "hayalet elle çizilir.");
                }

                return;
            }

            GameObject model = Instantiate(source.gameObject, root.transform);
            model.name = CONTROLLER_NAME;
            model.transform.localPosition = source.localPosition;
            model.transform.localRotation = source.localRotation;
            model.transform.localScale = source.localScale;

            var animator = model.GetComponent<Animator>();
            if (animator != null)
            {
                DestroyImmediate(animator);
            }
        }

        /// <summary>
        /// Kumanda kökünün başlangıç duruşu — üç kaynak, <b>sırayla</b>:
        /// (1) tanımdaki kayıt, (2) kabza parçasının kabaca ortası, (3) silahın biraz üstü.
        /// (2) ve (3)'te dönüş silahınkidir (kimlik kayıt): kök silahla hizalı doğar, yani "oluştur →
        /// yalnız taşı → Kaydet" silahı oyunda kumandayla hizalı bırakır.
        /// <para>⚠️ (1) kaydın birebir tersidir: "elleri oluştur → hiç dokunma → Kaydet" yazılı
        /// değeri DEĞİŞTİRMEZ. O kimlik bozulursa uzay yönlerinden biri ters demektir ve bakılacak
        /// tek yer bu dosyadaki <see cref="AnchorInItem"/> ile buradaki geri bileşimdir.</para>
        /// <para>⚠️ Bileşim <b>ölçeksizdir</b> (<c>TransformPoint</c> DEĞİL): kayıt METREdir ve
        /// <c>WPN_*</c> kökleri 0.8 ölçekli — ölçekli bileşim kökü silahtan 1/0.8 kadar uzağa koyar.</para>
        /// </summary>
        private static Pose ResolveStartPose(Transform weaponRoot, WeaponDefinition definition,
            GripSocketKind kind, bool rightHand)
        {
            if (definition.HasGrip(kind, rightHand))
            {
                Pose local = definition.GetGrip(kind, rightHand).LocalPose;
                return new Pose(
                    weaponRoot.position + weaponRoot.rotation * local.position,
                    weaponRoot.rotation * local.rotation);
            }

            // ⚠️ Önbellek YOK ve gerekmiyor: bu tarama el kurulurken kavrama noktası başına BİR kez
            // koşuyor (her karede koşan bir tüketicisi yok).
            Renderer part = SearchWeaponPart(weaponRoot,
                kind == GripSocketKind.Primary ? GRIP_KEYS : FOREGRIP_KEYS);

            Vector3 position = part != null
                ? part.bounds.center
                : weaponRoot.position + Vector3.up * 0.1f;

            return new Pose(position, weaponRoot.rotation);
        }

        /// <summary>
        /// Kumanda kökünün eşyaya göre yerel pozu — kaydın <b>tek</b> hesap yolu
        /// (<see cref="ItemGripPose"/>: anchor uzayı).
        /// <para>⚠️ <see cref="Transform.InverseTransformPoint"/> KULLANILMAZ: kayıt METREdir ve
        /// eşyanın görsel ölçeğiyle (<c>WPN_*</c> köklerinde 0.8) küçültülmemeli. Geri bileşim de
        /// aynı simetrik yolla yazılır (<see cref="ResolveStartPose"/>) — iki uç tek sözleşmede
        /// kalsın.</para>
        /// </summary>
        private static Pose AnchorInItem(Transform weaponRoot, Transform handRoot)
        {
            Quaternion inverse = Quaternion.Inverse(weaponRoot.rotation);
            return new Pose(
                inverse * (handRoot.position - weaponRoot.position),
                inverse * handRoot.rotation);
        }

        /// <summary>
        /// ⚠️ <see cref="HideFlags.DontSave"/> tüm alt ağaca yazılır: prefab kipi kaydedilirken tek
        /// bir el parçası bile dosyaya girmesin. Bayrak GameObject başınadır, kökte olması yetmez.
        /// <para>Kökün ALTINDAKİLER ayrıca <see cref="HideFlags.NotEditable"/> olur: hayalet el ve
        /// kumanda modeli görsel çocuklardır, kayda kökün pozu girer — çocuğu sürüklemek kaydı
        /// değiştirmez ve "kaydettim ama değişmedi" üretir; kilit o yolu baştan kapatır.</para>
        /// </summary>
        private static void MarkDontSave(GameObject root)
        {
            Transform[] all = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                all[i].gameObject.hideFlags = all[i].gameObject == root
                    ? HideFlags.DontSave
                    : HideFlags.DontSave | HideFlags.NotEditable;
            }
        }

        /// <summary>
        /// Hayalet el objesindeki ISDK bileşenlerini (<c>HandGhost</c>, <c>HandPuppet</c> …) Inspector'dan
        /// gizler: kullanıcı ele bakarken yalnız Transform + <see cref="GripHandAuthoring"/>
        /// görsün, yüzlerce satırlık Joint Maps listesini hiç açmasın. <c>null</c> ile sessizce geçer.
        /// <para>⚠️ Gizleme YALNIZ görseldir: bileşenler yerinde durur, <c>_puppet</c> referansı ve
        /// ISDK'nın poz uygulaması aynen çalışır. Bileşeni silmek eli çalışmaz yapardı.</para>
        /// </summary>
        private static void HideIsdkComponents(GameObject root)
        {
            if (root == null)
            {
                return;
            }

            Component[] components = root.GetComponents<Component>();
            for (int i = 0; i < components.Length; i++)
            {
                Component component = components[i];
                if (component == null || component is Transform || component is GripHandAuthoring)
                {
                    continue;
                }

                component.hideFlags |= HideFlags.HideInInspector;
            }
        }

        // ------------------------------------------------------------------------- aynalama

        /// <summary>
        /// Bir elin duruşunu KARŞI ele aynalar (eşya uzayında YZ düzlemine göre); karşı el yoksa
        /// kurar. Preset kaynaktan kopyalanır.
        /// <para>
        /// ⚠️ <b>ISDK'nın <c>MirrorHandGrabPose</c>'u KULLANILMAZ:</b> o, aynalanacak bir yüzey
        /// (<c>Grabbable</c> collider'ı) bulamadığında "best guess" adı altında keyfi bir dönüş
        /// uyguluyor — bizim tezgâhta hiç yüzey yok, yani her aynalama o tahmine düşerdi. Burada
        /// yapılan matematik tek satırdır ve gözle doğrulanabilir: <c>p=(x,y,z) → (−x,y,z)</c>,
        /// <c>q=(qx,qy,qz,qw) → (qx,−qy,−qz,qw)</c>.
        /// </para>
        /// <para>
        /// ⚠️ Aynalama bir <b>BAŞLANGIÇTIR</b>, son söz değil: silah kabzası sagital düzlemde
        /// yaklaşık simetriktir ama tetik, şarjör ve kurma kolu tek taraftadır — sonucu kullanıcı
        /// düzeltir.
        /// </para>
        /// </summary>
        internal static bool MirrorToOpposite(GripHandAuthoring source)
        {
            if (source == null)
            {
                return false;
            }

            PrefabStage stage = CurrentStage();
            Transform weaponRoot = StageWeaponRoot(stage);
            if (weaponRoot == null)
            {
                Debug.LogWarning($"{LOG} Aynalama için prefab kipi açık olmalı (referans silahın " +
                                 "kökü).");
                return false;
            }

            WeaponDefinition definition = ResolveDefinition(weaponRoot.gameObject);
            if (definition == null)
            {
                return false;
            }

            Pose local = AnchorInItem(weaponRoot, source.transform);
            var mirroredPosition = new Vector3(-local.position.x, local.position.y, local.position.z);
            var mirroredRotation = new Quaternion(
                local.rotation.x, -local.rotation.y, -local.rotation.z, local.rotation.w);

            GripHandAuthoring opposite = EnsureHand(stage, weaponRoot, definition,
                source.Kind, !source.RightHand);
            if (opposite == null)
            {
                return false;
            }

            opposite.transform.SetPositionAndRotation(
                weaponRoot.position + weaponRoot.rotation * mirroredPosition,
                weaponRoot.rotation * mirroredRotation);
            opposite.transform.localScale = Vector3.one;
            opposite.Preset = source.Preset;

            Selection.activeGameObject = opposite.gameObject;
            SceneView.RepaintAll();
            NotifyHandsChanged();
            return true;
        }

        // ----------------------------------------------------------------------------- kayıt

        /// <summary>
        /// Elin Inspector'ındaki "Kaydet (tüm eller)" düğmesinin kapısı — kullanıcı pencereye
        /// gitmeden kaydedebilsin.
        /// </summary>
        internal static bool SaveAll()
        {
            PrefabStage stage = CurrentStage();
            Transform weaponRoot = StageWeaponRoot(stage);
            if (weaponRoot == null)
            {
                Debug.LogWarning($"{LOG} Kaydetmek için prefab kipi açık olmalı.");
                return false;
            }

            WeaponDefinition definition = ResolveDefinition(weaponRoot.gameObject);
            if (definition == null)
            {
                Debug.LogWarning($"{LOG} Prefabın Weapon bileşeninde tanım yok — kaydedilecek asset " +
                                 "bulunamadı.");
                return false;
            }

            return SaveHands(weaponRoot, definition, FindHands(stage.scene));
        }

        /// <summary>
        /// Tezgâhtaki duruşu kalıcı veriye çevirir: her yaşayan el için kumanda kökünün eşyaya göre
        /// yerel pozu + preset → <c>WD_*.asset</c>.
        /// <para>
        /// ⚠️ <b>Prefab içeriğine hiçbir şey yazılmaz</b> ve prefab diske indirilmez: kaydın tek
        /// yeri tanımdır. Eller zaten stage sahnesinin ayrı kökleridir, yani kaydedilecek bir prefab
        /// değişikliği de yoktur.
        /// </para>
        /// <para>
        /// ⚠️ Play kipinde YAZILMAZ: kayıt <c>AssetDatabase</c> ile diske iniyor ve Play oturumunda
        /// yazılan değer bir sonraki domain reload'da belirsizleşir.
        /// </para>
        /// </summary>
        private static bool SaveHands(Transform weaponRoot, WeaponDefinition definition,
            List<GripHandAuthoring> hands)
        {
            if (EditorApplication.isPlaying)
            {
                Debug.LogWarning($"{LOG} Play kipinde kavrama yazılmaz.");
                return false;
            }

            Undo.RecordObject(definition, "Kavrama pozunu yaz");

            int written = 0;
            for (int i = 0; i < hands.Count; i++)
            {
                // Ölü girdi sessizce atlanır: hata satırı basmak, olmayan bir el için kullanıcıyı
                // aramaya gönderirdi (liste bu kare içinde bayatlamış olabiliyor).
                GripHandAuthoring hand = hands[i];
                if (hand == null)
                {
                    continue;
                }

                Pose local = AnchorInItem(weaponRoot, hand.transform);
                definition.EditorSetGrip(hand.Kind, hand.RightHand, local, hand.Preset);
                written++;
            }

            if (written == 0)
            {
                Debug.LogWarning($"{LOG} Tezgâhta el yok — hiçbir şey yazılmadı.");
                return false;
            }

            EditorUtility.SetDirty(definition);
            AssetDatabase.SaveAssets();

            Debug.Log($"{LOG} '{weaponRoot.name}' kavraması yazıldı: {written} el → " +
                      $"{definition.name}.asset", definition);
            return true;
        }

        private static WeaponDefinition ResolveDefinition(GameObject root)
        {
            if (root == null)
            {
                return null;
            }

            var weapon = root.GetComponent<Weapon>();
            return weapon == null ? null : weapon.Definition;
        }

        // ------------------------------------------------------------------------- el dalı

        /// <summary>
        /// ISDK hangi el dalında derlendi — <b>enum üyesinin varlığından</b> okunur:
        /// <c>HandJointId.HandPalm</c> yalnız OpenXR dalının tablosunda vardır.
        /// <para>⚠️ <b>Neden ölçüm, neden <c>#if</c> değil:</b> <c>ISDK_OPENXR_HAND</c> paketin
        /// kendi asmdef'inde <c>versionDefines</c> ile üretiliyor ve <b>yalnız o assembly'de</b>
        /// tanımlı. Bizim derlememizde her zaman false çıkardı, yani <c>#if</c> ile yazılan her
        /// satır sessizce yanlış dalı seçerdi. Enum üyesi ise aktif dalın tipinden geliyor: ad
        /// çözümlemesi hangi dal derlendiyse onun tablosuna düşüyor.</para>
        /// </summary>
        private static bool IsOpenXrHandBranch =>
            Enum.IsDefined(typeof(HandJointId), "HandPalm");

        /// <summary>
        /// Aktif el dalına ait model sağlayıcısını yükler; o yüklenemezse ötekine düşer.
        /// <para>⚠️ Sağlayıcı <b>dala aittir</b>: OpenXR iskeletiyle OVR modelini kurmak eli bozuk
        /// bir duruşa sokar (eklem sayısı ve sırası aynı değil).</para>
        /// </summary>
        private static bool TryGetGhostProvider(out HandGhostProvider provider)
        {
            string wanted = IsOpenXrHandBranch
                ? GHOST_PROVIDER_PATH_OPENXR
                : GHOST_PROVIDER_PATH_OVR;

            if (_ghostProvider == null || _ghostProviderPath != wanted)
            {
                _ghostProvider = AssetDatabase.LoadAssetAtPath<HandGhostProvider>(wanted);
                _ghostProviderPath = wanted;

                if (_ghostProvider == null)
                {
                    string fallback = wanted == GHOST_PROVIDER_PATH_OPENXR
                        ? GHOST_PROVIDER_PATH_OVR
                        : GHOST_PROVIDER_PATH_OPENXR;

                    _ghostProvider = AssetDatabase.LoadAssetAtPath<HandGhostProvider>(fallback);
                    if (_ghostProvider != null)
                    {
                        _ghostProviderPath = fallback;
                    }
                }
            }

            provider = _ghostProvider;
            if (provider != null)
            {
                _ghostProviderWarned = false;
                return true;
            }

            if (!_ghostProviderWarned)
            {
                _ghostProviderWarned = true;
                Debug.LogWarning($"{LOG} ISDK el sağlayıcısı iki dalda da bulunamadı " +
                                 $"({GHOST_PROVIDER_PATH_OPENXR} / {GHOST_PROVIDER_PATH_OVR}) — " +
                                 "paket sürümü değiştiyse yolları bu dosyadaki sabitlerden güncelle.");
            }

            return false;
        }

        /// <summary>
        /// Hangi dalın sağlayıcısının kullanıldığını tek satırda yazar.
        /// <para>⚠️ Bu satır süs değil <b>teşhistir</b>: yanlış dalın sağlayıcısı yüklendiğinde el
        /// yine kurulur, yalnız iskeleti tutmaz — belirtisi "el garip duruyor" olur ve sebebi hiçbir
        /// yerde görünmezdi.</para>
        /// </summary>
        private void DrawGhostSourceSection()
        {
            string asset = string.IsNullOrEmpty(_ghostProviderPath)
                ? "(henüz yüklenmedi)"
                : System.IO.Path.GetFileName(_ghostProviderPath);

            string branch = IsOpenXrHandBranch ? "OpenXR" : "OVR";

            EditorGUILayout.HelpBox($"El dalı: {branch} · el modeli: {asset}", MessageType.None);
        }

        // ---------------------------------------------------------------------- parça arama

        /// <summary>
        /// Silahın alt ağacında, adında anahtarlardan biri geçen ilk <see cref="Renderer"/>.
        /// Anahtarlar <b>sırayla</b> denenir, ilk tutan kazanır (spesifik olan listede önce durur).
        /// <para>Tek tüketicisi <see cref="ResolveStartPose"/>: kaydı olmayan silahta elin nereye
        /// konarak açılacağını çözerken kabza/ön kabza parçasının kabaca ortası kullanılıyor.</para>
        /// </summary>
        private static Renderer SearchWeaponPart(Transform weaponRoot, string[] keywords)
        {
            Renderer found = null;
            for (int i = 0; i < keywords.Length && found == null; i++)
            {
                found = SearchPartRenderer(weaponRoot, keywords[i]);
            }

            return found;
        }

        /// <summary>
        /// Ada göre derinlik-öncelikli arama; <see cref="IsPartSearchNoise"/> dediği dallara girmez.
        /// </summary>
        private static Renderer SearchPartRenderer(Transform node, string keyword)
        {
            if (IsPartSearchNoise(node))
            {
                return null;
            }

            if (node.name.ToLowerInvariant().Contains(keyword))
            {
                var renderer = node.GetComponent<Renderer>();
                if (renderer != null)
                {
                    return renderer;
                }
            }

            for (int i = 0; i < node.childCount; i++)
            {
                Renderer hit = SearchPartRenderer(node.GetChild(i), keyword);
                if (hit != null)
                {
                    return hit;
                }
            }

            return null;
        }

        /// <summary>
        /// Parça aramasına girmemesi gereken dallar: prefabta kalmış <b>el modelleri</b> ve kavrama
        /// çerçevesi.
        /// <para>⚠️ Bu eleme olmadan araç <b>kendi kurduğu şeyi</b> kabza sanabilir — el modelinin ve
        /// çerçeve prefabının altında da Renderer var ve adları aranan anahtarlarla çakışabiliyor
        /// (yeni el o zaman kendi eski elinin üstünde açılırdı).</para>
        /// </summary>
        private static bool IsPartSearchNoise(Transform node)
        {
            if (node.name == ItemHandRig.RootNodeName || node.name.StartsWith(HAND_ROOT_PREFIX))
            {
                return true;
            }

            return node.GetComponent<WeaponFrame>() != null;
        }
    }
}
