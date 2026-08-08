using System.Collections.Generic;
using System.Reflection;
using Oculus.Interaction;
using Oculus.Interaction.HandGrab;
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
    /// kendiliğinden tanır → <b>Elleri Oluştur</b> → sağ ve sol hayalet eller sahnede belirir →
    /// elleri kabzalara sürükle, parmakları Inspector'daki <see cref="GripHandAuthoring"/>'den
    /// (ya da Hierarchy'den kemiği seçip) bük → istersen <b>Aynala</b> → <b>Kaydet</b>.
    /// </para>
    /// <para>
    /// ⚠️ <b>Kullanıcının sürüklediği el kökü ISDK BİLEK çerçevesidir</b> ve başka hiçbir şeye
    /// çevrilmez. Eski tezgâh eli kumanda-anchor çerçevesinde tutup modeli
    /// <see cref="HandGripConvention.Correction"/> ile çeviriyordu; o düzeltme <b>ölçülmemiş
    /// ergonomik tahmin</b> sabitlerinden türüyor, yani authoring döngüsüne girdiği anda kullanıcı
    /// gözüyle "düzgün" gördüğü eli tahminin hatası kadar yanlış yere koyuyordu (belirtisi: silah
    /// oyunda 90° dönük). Bugün <c>Correction</c> yalnız İKİNCİL <c>WD_*</c> alanları yazılırken,
    /// tek yönde kullanılır.
    /// </para>
    /// <para>
    /// ⚠️ <b>Tezgâhtaki el SİLAHI DÖNDÜRMEZ</b> (<see cref="ItemGripAuthority"/> sözleşmesi):
    /// silahın kumandaya göre rotasyonu <c>WD_*</c>'daki elle ayarlanan euler'dir (kimlik =
    /// kumandayla birebir), elin yerleşimi ise (1) el MODELİNİN silah üstündeki duruşunu ve
    /// (2) silahın hangi NOKTADAN avuca oturacağını belirler. Yani eli kabzada döndürmek oyunda
    /// yalnız el görselini döndürür, namlunun baktığı yönü değiştirmez.
    /// </para>
    /// <para>
    /// ⚠️ <b>Eller prefabın İÇİNE girmez</b>: her el, prefab stage sahnesinin ayrı bir KÖK objesidir
    /// (<see cref="HideFlags.DontSave"/>). Prefab kipinde diske yalnız <c>prefabContentsRoot</c>
    /// altındaki ağaç yazılır — düzenleme ortamının kendi objeleri de tam olarak böyle durur. El
    /// prefabın altına asılsaydı ilk kaydetmede silahın içine bir el modeli girer ve arenada havada
    /// el olarak görünürdü.
    /// </para>
    /// <para>
    /// <b>Aracın var olma sebebi geri besleme süresidir:</b> kavrama ofseti tek doğru değeri olan
    /// bir sayı değil, "avuç kabzaya değiyor mu / işaret parmağı tetiğe ulaşıyor mu" sorusunun
    /// cevabıdır. O soruyu APK build'i + gözlük turuyla sormak her denemeyi dakikalara çıkarıyordu.
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
        /// <see cref="HandGrabPose"/>'un OpenXR dalında kullandığı poz alanı — <b>ölçüm sondası</b>.
        /// <para>
        /// ⚠️ ISDK aynı sınıfta iki poz alanı taşıyor (<c>_handPose</c> = OVR, <c>_targetHandPose</c>
        /// = OpenXR) ve hangisinin canlı olduğunu <c>ISDK_OPENXR_HAND</c> belirliyor. O tanım
        /// paketin <b>kendi</b> asmdef'inde <c>versionDefines</c> ile üretiliyor, yani BİZİM
        /// derlememizde yoktur: burada <c>#if</c> yazmak her zaman yanlış dalı seçerdi. Dalı bu
        /// yüzden tahmin etmiyoruz, <see cref="TryMeasureHandBranch"/> ile <b>ölçüyoruz</b>.
        /// </para>
        /// </summary>
        private static readonly FieldInfo TargetHandPoseField = typeof(HandGrabPose)
            .GetField("_targetHandPose", BindingFlags.NonPublic | BindingFlags.Instance);

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
        [System.NonSerialized] private bool _handsSeen;

        /// <summary>Eller pencere dışında bir sebeple kalktı — bildirim yeniden kurulana dek durur.</summary>
        [System.NonSerialized] private bool _handsLost;

        private static HandGhostProvider _ghostProvider;

        /// <summary>Yüklü sağlayıcının yolu — dal ölçümü değişince yeniden yükleneceğini bilmek için.</summary>
        private static string _ghostProviderPath;

        /// <summary>
        /// Sağlayıcı hiç bulunamadığında uyarı bir kez basılsın diye.
        /// <para>⚠️ Bu yol <c>OnGUI</c>'den her karede geçebiliyor: bayraksız hâlde eksik bir asset
        /// konsolu saniyede onlarca satırla boğardı.</para>
        /// </summary>
        private static bool _ghostProviderWarned;

        /// <summary>
        /// ISDK OpenXR dalında mı derlendi — <c>null</c> ise <b>henüz ölçülemedi</b>.
        /// <para>⚠️ Ölçülemeyen sonuç önbelleğe ALINMAZ: sonda olarak kullanılan düğümün
        /// <c>_usesHandPose</c>'u kapalıysa <see cref="HandGrabPose.HandPose"/> her iki dalda da
        /// <c>null</c> döner ve o an alınan "cevap" dala değil düğüme ait olurdu.</para>
        /// </summary>
        private static bool? _openXrHandBranch;

        // --------------------------------------------------------------------------- pencere

        // Öncelik 24: 23'ü NetItemIdGuard'ın "Rebuild Net Item Catalog"u kullanıyor. Aynı sayı
        // Unity'de hata üretmez ama iki öğenin sırası belirsizleşir — menü sırası okuyanın kafasında
        // "hangisi önce" sorusu doğurmasın.
        [MenuItem("Tools/VortexArena/Weapons/Kavrama Pozu Stüdyosu", false, 24)]
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
            // Parmak slider'ları DontSave kemiklere yazdığı için sahneyi kirletmez ve kaydı hiç
            // tetiklemez; elin transformu / prefab içeriği kirletir ve tetikler.
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
                                    scene.path.EndsWith(".prefab", System.StringComparison.OrdinalIgnoreCase) ||
                                    (stage != null && stage.scene == scene);
                if (previewScene)
                {
                    return;
                }

                DestroyHands(scene);
            };
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
                HideIsdkComponents(go);
                // Çevirme bileşen bayraklarına da bulaşırsa kullanıcıya kalan iki bileşen
                // (Transform + slider'lar) Inspector'da kilitli/gizli kalmasın.
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
                    "Play kipinde kavrama yazılmaz — çalışan oyunda elin gerçeği sentetik eldir.",
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
                "Akış: prefabı prefab kipinde aç → Elleri Oluştur → elleri kabzalara oturt, " +
                "parmakları bük → (istersen) Aynala → Kaydet.",
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
                // yazılmış silahta el kayıtlı poz düğümünden aynı yere geri gelir.
                EditorGUILayout.HelpBox(
                    "Eller tezgâhtan kalktı (prefab kipi içeriği yeniden yüklenmiş olabilir). " +
                    "Yeniden oluştur: kavraması yazılmış bir silahta eller kayıtlı poz düğümünden " +
                    "aynı yere gelir.",
                    MessageType.Info);
            }

            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(live == 0))
                {
                    if (GUILayout.Button("Kaydet", GUILayout.Height(26f)))
                    {
                        SaveHands(stage, weaponRoot, definition, hands);
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
                "Yazan tek düğme Kaydet'tir. Parmakları elin Inspector'ındaki GripHandAuthoring " +
                "bileşeninden (ya da Hierarchy'den kemiği seçip) bükersin; Kaydet HER ZAMAN " +
                "kemiklerin o andaki gerçek duruşunu okur.",
                MessageType.None);
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
        /// Kaydedilecek sayıları <b>canlı</b> gösterir.
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

                Vector3 localPosition = weaponRoot.InverseTransformPoint(hand.transform.position);
                Quaternion localRotation =
                    Quaternion.Inverse(weaponRoot.rotation) * hand.transform.rotation;

                EditorGUILayout.LabelField(
                    ItemGripPoses.NodeName(hand.Kind, hand.RightHand),
                    $"{Format(localPosition)}  {Format(localRotation.eulerAngles)}");
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("WD_* kavrama alanları (sağ elden türetilir)",
                EditorStyles.miniBoldLabel);

            DrawGripFieldPreview(weaponRoot, hands, definition, GripSocketKind.Primary);
            if (definition.IsTwoHanded)
            {
                DrawGripFieldPreview(weaponRoot, hands, definition, GripSocketKind.Secondary);
            }
        }

        private void DrawGripFieldPreview(Transform weaponRoot, List<GripHandAuthoring> hands,
            WeaponDefinition definition, GripSocketKind kind)
        {
            GripHandAuthoring right = FindHand(hands, kind, true);

            if (kind == GripSocketKind.Primary)
            {
                if (right == null)
                {
                    EditorGUILayout.LabelField("primaryGrip*", "(sağ el yok)");
                    return;
                }

                Vector3 primaryPosition = ItemHandGripBake.PrimaryPositionFromWrist(weaponRoot,
                    right.transform.position, definition.PrimaryGripRotation);
                EditorGUILayout.LabelField("primaryGripPosition", Format(primaryPosition));
                EditorGUILayout.LabelField("primaryGripEuler",
                    $"{Format(definition.PrimaryGripRotation.eulerAngles)}  (elle ayar — Kaydet dokunmaz)");
                return;
            }

            if (right == null || !right.HasBindBoneBasis)
            {
                EditorGUILayout.LabelField("secondaryGrip*", "(sağ el yok / bazı ölçülemedi)");
                return;
            }

            ItemHandGripBake.FromWrist(weaponRoot, AnchorProxy(right), kind,
                out Vector3 position, out Vector3 euler);

            EditorGUILayout.LabelField("secondaryGripPosition", Format(position));
            EditorGUILayout.LabelField("secondaryGripEuler", Format(euler));
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
        /// ⚠️ El, ISDK hayalet elinin KENDİSİDİR: kök transformu doğrudan bilek çerçevesi olsun diye
        /// ara bir düğüm açılmaz. Ara düğüm, sürüklenen şeyle kaydedilen şey arasına sessizce bir
        /// ofset girme ihtimali demektir.
        /// </para>
        /// <para>
        /// ⚠️ İskelet çözümü ve eksen ölçümü el <b>poza sokulmadan ÖNCE</b> yapılır
        /// (<see cref="GripHandAuthoring.Resolve"/>): bükülmüş bir elden ölçülen baz o duruşu içerir
        /// ve hem parmak slider'ları hem <c>WD_*</c> alanları kalıcı olarak yanlış çıkardı.
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
                MarkDontSave(existing.gameObject);
                HideIsdkComponents(existing.gameObject);
                return existing;
            }

            if (!TryGetGhostProvider(weaponRoot, out HandGhostProvider provider))
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

            HandGhost ghost = Instantiate(prototype);
            GameObject go = ghost.gameObject;
            go.name = HandRootName(kind, rightHand);
            SceneManager.MoveGameObjectToScene(go, stage.scene);
            go.transform.localScale = Vector3.one;

            HandPuppet puppet = ghost.GetComponent<HandPuppet>();
            var authoring = go.AddComponent<GripHandAuthoring>();
            if (authoring == null)
            {
                // ⚠️ Yarım el BIRAKILMAZ: bileşensiz ghost'u FindHands göremez (kimliği bileşende),
                // yani ne pencerede listelenir ne "Elleri Temizle" ile silinebilir — sahnede
                // kaydedilmemiş, sahipsiz bir el modeli olarak kalır ve ilk stage/Play geçişinde
                // sessizce kaybolur. Kurulamayan eli hemen yok etmek o hayaleti hiç üretmez.
                DestroyImmediate(go);
                Debug.LogError($"{LOG} {kind}/{(rightHand ? "sağ" : "sol")} el kurulamadı: " +
                               "GripHandAuthoring eklenemedi. Sınıf RUNTIME asmdef'inde " +
                               "(VortexArena.Core, #if UNITY_EDITOR sarmalında) olmalı — Unity " +
                               "editör asmdef'inde derlenen bir MonoBehaviour'ı AddComponent ile " +
                               "kabul etmez ve null döner.");
                return null;
            }

            authoring.Resolve(puppet, kind, rightHand);
            HideIsdkComponents(go);

            // ⚠️ Prefabtaki poz KOPYALANIR, doğrudan verilmez: ISDK'nın puppet'ı verilen pozu
            // yerinde değiştirebiliyor ve kaynak prefabın KENDİ verisi. Kopyalamadan geçirmek, el
            // kurmanın prefabı sessizce kirletmesi demek olurdu.
            HandGrabPose node = FindPoseNode(weaponRoot, kind, rightHand);
            HandPose source = node != null ? node.HandPose : null;
            HandPose startPose = source != null
                ? new HandPose(source)
                : CreateDefaultHandPose(handedness);

            ghost.SetPose(startPose, ResolveStartPose(weaponRoot, definition, kind, rightHand,
                authoring, node));

            authoring.CaptureBaseline();
            MarkDontSave(go);
            NotifyHandsChanged();
            return authoring;
        }

        /// <summary>
        /// Elin başlangıç duruşu — üç kaynak, <b>sırayla</b>:
        /// (1) yerleştirilmiş poz düğümü, (2) tanımın kavrama alanları, (3) kabza parçasının kabaca
        /// ortası.
        /// <para>⚠️ (1) ve (2) kaydın birebir tersidir: "elleri oluştur → hiç dokunma → Kaydet" yazılı
        /// değeri DEĞİŞTİRMEZ. O kimlik bozulursa uzay yönlerinden biri ters demektir ve bakılacak
        /// tek yer <see cref="ItemHandGripBake"/>'tir.</para>
        /// </summary>
        private static Pose ResolveStartPose(Transform weaponRoot, WeaponDefinition definition,
            GripSocketKind kind, bool rightHand, GripHandAuthoring authoring, HandGrabPose node)
        {
            if (node != null && !ItemGripPoses.IsUnplaced(node))
            {
                // ⚠️ ÖLÇEKLİ bileşim: ISDK'nın RelativePose'u PoseUtils.DeltaScaled ile üretiliyor,
                // ölçeksiz geri bileşim eli silahtan 1/0.8 kadar uzağa koyardı.
                Transform reference = node.RelativeTo != null ? node.RelativeTo : weaponRoot;
                return PoseUtils.GlobalPoseScaled(reference, node.RelativePose);
            }

            if (authoring.HasBindBoneBasis && HasAuthoredGrip(definition, kind))
            {
                ItemHandGripBake.ToWristLocal(definition, kind,
                    out Vector3 localPosition, out Quaternion localRotation);

                Quaternion anchorRotation = weaponRoot.rotation * localRotation;
                return new Pose(
                    weaponRoot.position + weaponRoot.rotation * localPosition,
                    anchorRotation * HandGripConvention.Correction(rightHand, authoring.BindBoneBasis));
            }

            // ⚠️ Önbellek YOK ve gerekmiyor: bu tarama el kurulurken kavrama noktası başına BİR kez
            // koşuyor (her karede koşan bir tüketicisi kalmadı).
            Renderer part = SearchWeaponPart(weaponRoot,
                kind == GripSocketKind.Primary ? GRIP_KEYS : FOREGRIP_KEYS);

            Vector3 position = part != null
                ? part.bounds.center
                : weaponRoot.position + Vector3.up * 0.1f;

            return new Pose(position, weaponRoot.rotation);
        }

        /// <summary>Tanımda o kavrama noktası için elle yazılmış bir duruş var mı.</summary>
        private static bool HasAuthoredGrip(WeaponDefinition definition, GripSocketKind kind)
        {
            if (kind == GripSocketKind.Secondary)
            {
                return definition.SecondaryGripPosition.sqrMagnitude > 1e-8f ||
                       Quaternion.Angle(definition.SecondaryGripRotation, Quaternion.identity) > 0.01f;
            }

            return definition.PrimaryGripPosition.sqrMagnitude > 1e-8f ||
                   Quaternion.Angle(definition.PrimaryGripRotation, Quaternion.identity) > 0.01f;
        }

        /// <summary>
        /// ⚠️ <see cref="HideFlags.DontSave"/> tüm alt ağaca yazılır: prefab kipi kaydedilirken tek
        /// bir el parçası bile dosyaya girmesin. Bayrak GameObject başınadır, kökte olması yetmez.
        /// </summary>
        private static void MarkDontSave(GameObject root)
        {
            Transform[] all = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                all[i].gameObject.hideFlags = HideFlags.DontSave;
            }
        }

        /// <summary>
        /// Elin kökündeki ISDK bileşenlerini (<c>HandGhost</c>, <c>HandPuppet</c> …) Inspector'dan
        /// gizler: kullanıcı eli seçtiğinde yalnız Transform + <see cref="GripHandAuthoring"/>
        /// görsün, yüzlerce satırlık Joint Maps listesini hiç açmasın.
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
        /// Bir elin duruşunu ve parmaklarını KARŞI ele aynalar; karşı el yoksa kurar.
        /// <para>
        /// ⚠️ <b>Ayna matematiği ELLE YAZILMAZ:</b> ISDK'nın kendi
        /// <see cref="HandGrabUtils.MirrorHandGrabPose"/>'u kullanılır — ayna ekseni, el dalına göre
        /// değişen "keyfi dönüş" ve eklem aynalaması onun içinde duruyor. Kendi uygulamamız, ISDK
        /// dalı değiştiğinde sessizce yanlış tarafa düşerdi.
        /// </para>
        /// <para>
        /// ⚠️ Aynalama bir BAŞLANGIÇTIR, son söz değil: iki elin kabzaları simetrik değildir
        /// (tetik, şarjör, kurma kolu tek taraftadır) ve kullanıcı sonucu elle düzeltir. Bu yüzden
        /// aynalanan el "yazılmış" sayılır ve Kaydet onu kendi duruşundan okur.
        /// </para>
        /// </summary>
        internal static bool MirrorToOpposite(GripHandAuthoring source)
        {
            if (source == null || source.Puppet == null)
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
            var temp = new GameObject("[VA AynaGecici]") { hideFlags = HideFlags.HideAndDontSave };

            try
            {
                HandGrabPose from = HandGrabUtils.CreateHandGrabPose(temp.transform, weaponRoot);
                from.transform.SetPositionAndRotation(
                    source.transform.position, source.transform.rotation);

                HandPose sourcePose = CreateDefaultHandPose(source.Handedness);
                sourcePose.Handedness = source.Handedness;
                source.Puppet.CopyCachedJoints(ref sourcePose);
                source.WriteFreedom(sourcePose.FingersFreedom);
                from.InjectOptionalHandPose(sourcePose);

                HandGrabPose to = HandGrabUtils.CreateHandGrabPose(temp.transform, weaponRoot);
                HandGrabUtils.MirrorHandGrabPose(from, to, weaponRoot);

                GripHandAuthoring opposite = EnsureHand(stage, weaponRoot, definition,
                    source.Kind, !source.RightHand);

                if (opposite == null || opposite.Puppet == null)
                {
                    return false;
                }

                opposite.transform.SetPositionAndRotation(
                    to.transform.position, to.transform.rotation);
                opposite.transform.localScale = Vector3.one;

                HandPose mirrored = to.HandPose;
                if (mirrored != null)
                {
                    opposite.Puppet.SetJointRotations(mirrored.JointRotations);
                }

                opposite.CopyFreedomFrom(source);

                // ⚠️ Aynalanan duruş yeni BAZ olur: slider'lar sıfırlanmasaydı kullanıcının kaynak
                // eldeki kıvrımı, aynalanmış elin üstüne ikinci kez binerdi.
                opposite.CaptureBaseline();

                Selection.activeGameObject = opposite.gameObject;
                NotifyHandsChanged();
                return true;
            }
            finally
            {
                DestroyImmediate(temp);
            }
        }

        // ----------------------------------------------------------------------------- kayıt

        /// <summary>
        /// Tezgâhtaki duruşu kalıcı veriye çevirir: el pozları + parmaklar → <c>GripPoses/Pose_*</c>
        /// (prefab içeriği), silahın bileğe göre duruşu → <c>WD_*.asset</c> kavrama alanları.
        /// <para>
        /// ⚠️ Yazım <b>açık prefab kipinin KENDİ içeriğine</b> yapılır (<c>prefabContentsRoot</c>) ve
        /// sahne kirli işaretlenir; headless <c>LoadPrefabContents</c> yolu KULLANILMAZ: aynı prefab
        /// o an kipte açıkken ikinci bir kopyaya yazmak, kullanıcının ekranındaki hâlin kaydı
        /// sessizce ezmesiyle biterdi.
        /// </para>
        /// <para>
        /// ⚠️ Poz düğümünün yerel konumu <see cref="Transform.InverseTransformPoint"/> ile
        /// hesaplanır — <b>bilerek ölçekli</b>. ISDK'nın <c>HandGrabPose.RelativePose</c>'u da öyle
        /// (<c>PoseUtils.DeltaScaled</c>); "ofset metredir, ölçeklenmez" kuralı tanımın kavrama
        /// alanları içindir, ISDK'nın kendi sözleşmesi için değil. İki taraf aynı sözleşmede
        /// olmazsa el silahtan 1/0.8 kadar uzağa yapışır.
        /// </para>
        /// </summary>
        private void SaveHands(PrefabStage stage, Transform weaponRoot, WeaponDefinition definition,
            List<GripHandAuthoring> hands)
        {
            Undo.RegisterFullObjectHierarchyUndo(weaponRoot.gameObject, "Kavrama pozunu yaz");

            var writtenKinds = new HashSet<GripSocketKind>();

            for (int i = 0; i < hands.Count; i++)
            {
                // Ölü girdi sessizce atlanır: hata satırı basmak, olmayan bir el için kullanıcıyı
                // aramaya gönderirdi (liste bu kare içinde bayatlamış olabiliyor).
                if (hands[i] == null)
                {
                    continue;
                }

                if (WritePoseNode(weaponRoot, hands[i]))
                {
                    writtenKinds.Add(hands[i].Kind);
                }
            }

            int mirrored = 0;
            foreach (GripSocketKind kind in writtenKinds)
            {
                mirrored += CompletePair(weaponRoot, hands, kind);
            }

            int gripFields = WriteGripFields(weaponRoot, definition, hands);

            int legacy = RemoveLegacyHandRig(weaponRoot);
            legacy += RemoveStrayAuthoringNodes(weaponRoot);

            EditorSceneManager.MarkSceneDirty(stage.scene);

            string legacyNote = legacy > 0
                ? $" Prefabta kalmış {legacy} eski el düğümü silindi."
                : string.Empty;
            string mirrorNote = mirrored > 0
                ? $" {mirrored} karşı el düğümü ISDK aynasıyla üretildi."
                : string.Empty;

            Debug.Log($"{LOG} '{weaponRoot.name}' kavraması yazıldı: {hands.Count} el pozu, " +
                      $"{gripFields} kavrama alanı çifti.{mirrorNote}{legacyNote} " +
                      "Prefab kipi KİRLİ işaretlendi — diske yazılması için prefabı kaydet " +
                      "(Auto Save açıksa kendiliğinden yazılır).", definition);
        }

        /// <summary>
        /// Bir elin poz düğümünü yazar.
        /// <para>
        /// ⚠️ <b>Parmak rotasyonları <see cref="HandPuppet.CopyCachedJoints"/> ile okunur, ham
        /// <c>localRotation</c> ile DEĞİL.</b> ISDK sözleşmesi asimetrik: uygularken
        /// <c>localRotation = RotationOffset * jointRotations[i]</c> (bkz.
        /// <see cref="HandPuppet.SetJointRotations"/>), okurken bu yüzden ofsetin TERSİ alınmalı
        /// (<see cref="HandJointMap.TrackedRotation"/>). Ham okuma, kaydedilen her poza modelin
        /// eklem ofsetini bir kez daha bindirir — belirtisi "parmaklar kaydettiğimden başka türlü
        /// kıvrılıyor" olur ve her Kaydet'te sapma büyür.
        /// </para>
        /// </summary>
        private static bool WritePoseNode(Transform weaponRoot, GripHandAuthoring hand)
        {
            if (hand == null || hand.Puppet == null || hand.Puppet.JointMaps == null)
            {
                // ⚠️ `?.` KULLANILMAZ: Unity'nin yok edilmiş nesne için özelleştirdiği `==`
                // operatörünü atlar ve ölü bir referansta gerçek bir NullReferenceException atardı.
                string label = hand == null ? "(el yok)" : hand.name;
                Debug.LogError($"{LOG} {label}: HandPuppet yok — parmak rotasyonları okunamadı.");
                return false;
            }

            HandGrabPose node = EnsurePoseNode(weaponRoot, hand.Kind, hand.RightHand);

            node.transform.localPosition = weaponRoot.InverseTransformPoint(hand.transform.position);
            node.transform.localRotation =
                Quaternion.Inverse(weaponRoot.rotation) * hand.transform.rotation;
            node.transform.localScale = Vector3.one;

            if (node.RelativeTo != weaponRoot)
            {
                node.InjectRelativeTo(weaponRoot);
            }

            HandPose pose = node.HandPose ?? CreateDefaultHandPose(hand.Handedness);
            pose.Handedness = hand.Handedness;
            hand.Puppet.CopyCachedJoints(ref pose);
            hand.WriteFreedom(pose.FingersFreedom);
            node.InjectOptionalHandPose(pose);

            EditorUtility.SetDirty(node);
            return true;
        }

        /// <summary>
        /// Bir kavrama noktasının yalnız TEK eli tezgâhtaysa karşı düğümü ISDK aynasıyla üretir.
        /// <para>⚠️ İki kapı da kapalı olmalı: karşı el tezgâhtaysa aynalama YAPILMAZ (o el kendi
        /// duruşundan yazıldı ve ayna onu ezerdi), karşı düğüm zaten YERLEŞTİRİLMİŞSE de yapılmaz
        /// (daha önce elle ayarlanmış bir kavramayı, bu koşuda ona hiç dokunulmadığı hâlde
        /// değiştirmek olurdu). Yani otomatik tamamlama yalnız "hiç yazılmamış" tarafı doldurur.</para>
        /// </summary>
        private static int CompletePair(Transform weaponRoot, List<GripHandAuthoring> hands,
            GripSocketKind kind)
        {
            int produced = 0;

            for (int side = 0; side < 2; side++)
            {
                bool rightHand = side == 0;
                if (FindHand(hands, kind, !rightHand) != null)
                {
                    continue; // karşı el tezgâhta — kendi duruşundan yazıldı
                }

                GripHandAuthoring authored = FindHand(hands, kind, rightHand);
                if (authored == null)
                {
                    continue;
                }

                HandGrabPose opposite = FindPoseNode(weaponRoot, kind, !rightHand);
                if (opposite != null && opposite.HandPose != null &&
                    !ItemGripPoses.IsUnplaced(opposite))
                {
                    continue; // karşı düğüm daha önce yazılmış — ayna onu ezmez
                }

                HandGrabPose source = FindPoseNode(weaponRoot, kind, rightHand);
                if (source == null || source.HandPose == null)
                {
                    continue;
                }

                HandGrabPose mirror = EnsurePoseNode(weaponRoot, kind, !rightHand);
                HandGrabUtils.MirrorHandGrabPose(source, mirror, weaponRoot);
                EditorUtility.SetDirty(mirror);
                produced++;

                Debug.Log($"{LOG} {ItemGripPoses.NodeName(kind, !rightHand)} tezgâhta yoktu — " +
                          "ISDK aynasıyla üretildi. Beğenmezsen o eli de oluşturup elle ayarla.");
            }

            return produced;
        }

        /// <summary>
        /// <c>WD_*</c> kavrama alanlarını SAĞ elden türetip yazar.
        /// <para>
        /// ⚠️ Bu alanlar artık yerel oyuncunun elini sürmüyor (onu <c>GripPoses/Pose_*</c> yapıyor):
        /// kavrama <b>soketinin</b> yerini ve rig'i olmayan uçların (admin gözlemci, uzak avatarın
        /// elindeki silah) fallback çizimini besliyorlar. Yine de yazılır — yazılmazsa o uçlarda
        /// silah elin içinde/dışında durur.
        /// </para>
        /// <para>
        /// ⚠️ <b><c>primaryGripEuler</c>'a DOKUNULMAZ:</b> o alan "silah kumanda anchor'ına göre
        /// hangi açıda durur" sorusunun TEK ve ELLE ayarlanan cevabıdır (kimlik = kumandayla
        /// birebir aynı eksenler) — eşyanın rotasyonu tezgâhtaki elden TÜREMEZ, el yalnız el
        /// modelinin duruşunu tarif eder (<see cref="ItemGripAuthority"/> ile aynı sözleşme).
        /// <c>primaryGripPosition</c> elin bilek NOKTASINDAN türetilir: fallback uçlarda da elin
        /// oturduğu kabza noktası avuca gelsin. İkincil alanlar (ön kabza noktası + uzak elin
        /// duruşu) bugünkü gibi elden yazılır; oradaki anchor çevirisi
        /// <see cref="HandGripConvention.Correction"/>'dan geçer ve bazı ölçülemeyen elde alan
        /// YAZILMAZ (yanlış bir kavrama alanı, boş olandan daha zor teşhis edilir).
        /// </para>
        /// </summary>
        private static int WriteGripFields(Transform weaponRoot, WeaponDefinition definition,
            List<GripHandAuthoring> hands)
        {
            var so = new SerializedObject(definition);
            int written = 0;

            written += WriteGripField(so, weaponRoot, hands, GripSocketKind.Primary);
            if (definition.IsTwoHanded)
            {
                written += WriteGripField(so, weaponRoot, hands, GripSocketKind.Secondary);
            }

            if (written > 0)
            {
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(definition);
                AssetDatabase.SaveAssets();
            }

            return written;
        }

        private static int WriteGripField(SerializedObject so, Transform weaponRoot,
            List<GripHandAuthoring> hands, GripSocketKind kind)
        {
            GripHandAuthoring right = FindHand(hands, kind, true);
            if (right == null)
            {
                Debug.Log($"{LOG} {kind}: sağ el tezgâhta yok — WD kavrama alanları dokunulmadan " +
                          "bırakıldı (poz düğümü yine yazıldı).");
                return 0;
            }

            if (kind == GripSocketKind.Primary)
            {
                // Rotasyon elden TÜREMEZ (yalnız elle ayarlanan euler); pozisyon elin bilek
                // noktasından, MEVCUT euler'e göre türetilir — gerekçe WriteGripFields'ta.
                Quaternion gripRotation =
                    Quaternion.Euler(so.FindProperty("primaryGripEuler").vector3Value);
                so.FindProperty("primaryGripPosition").vector3Value =
                    ItemHandGripBake.PrimaryPositionFromWrist(weaponRoot,
                        right.transform.position, gripRotation);
                return 1;
            }

            if (!right.HasBindBoneBasis)
            {
                Debug.LogWarning($"{LOG} {kind}: elin anatomik bazı ölçülemedi — WD kavrama alanları " +
                                 "YAZILMADI (yanlış yazmaktansa dokunmamak).");
                return 0;
            }

            ItemHandGripBake.FromWrist(weaponRoot, AnchorProxy(right), kind,
                out Vector3 gripPosition, out Vector3 gripEuler);

            so.FindProperty("secondaryGripPosition").vector3Value = gripPosition;
            so.FindProperty("secondaryGripEuler").vector3Value = gripEuler;

            return 1;
        }

        /// <summary>
        /// Elin bileğinden türetilmiş <b>kumanda anchor'ı</b> pozu.
        /// <para>⚠️ Konum aynen bileğin konumudur (<c>HandGripPivot.PalmOffset</c> bugün sıfır ve
        /// buraya EKLENMEZ): anchor'dan bileğe giden ofseti burada bir kez daha uygulamak onu iki kez
        /// saymak olurdu — bugün görünmez, ofset ölçülüp doldurulduğu gün el silahın önüne kayardı ve
        /// sebebi görünmezdi.</para>
        /// </summary>
        private static Pose AnchorProxy(GripHandAuthoring hand)
        {
            Quaternion correction = HandGripConvention.Correction(hand.RightHand, hand.BindBoneBasis);
            return new Pose(
                hand.transform.position,
                hand.transform.rotation * Quaternion.Inverse(correction));
        }

        /// <summary>
        /// Prefabta kalan <c>Hands/Hand_*</c> ağacı ölü veridir ve silinir.
        /// <para>⚠️ Sessizce BIRAKILMAZ: o düğümler kavramanın ikinci (ve artık okunmayan) bir
        /// tarifidir; duran bir kopya, "hangisi geçerli" sorusunu her açanın kafasında yeniden
        /// doğurur. Runtime emniyeti (<see cref="ItemHandRig.HideAll"/>) eski prefablar için
        /// yerinde kalır.</para>
        /// </summary>
        private static int RemoveLegacyHandRig(Transform itemRoot)
        {
            Transform node = itemRoot.Find(ItemHandRig.RootNodeName);
            if (node == null)
            {
                return 0;
            }

            DestroyImmediate(node.gameObject, true);
            return 1;
        }

        /// <summary>
        /// Prefabın İÇİNE kazara sürüklenmiş tezgâh düğümlerini siler.
        /// <para>⚠️ Bu emniyet gerçek bir riski karşılıyor: eller sahnenin ayrı kökleridir ama
        /// Hierarchy'de sürüklenerek prefabın altına taşınabilirler. O hâlde <c>DontSave</c> bayrağı
        /// onları kurtarmaz (prefab kipi ağacı olduğu gibi yazar) ve silahın içine bir el modeli
        /// girer — arenada havada duran bir el olarak görünürdü.</para>
        /// </summary>
        private static int RemoveStrayAuthoringNodes(Transform itemRoot)
        {
            int removed = 0;
            Transform[] all = itemRoot.GetComponentsInChildren<Transform>(true);

            for (int i = all.Length - 1; i >= 0; i--)
            {
                Transform node = all[i];
                if (node == null || node == itemRoot)
                {
                    continue;
                }

                bool stray = node.name.StartsWith(HAND_ROOT_PREFIX) ||
                             node.GetComponent<GripHandAuthoring>() != null;
                if (!stray)
                {
                    continue;
                }

                DestroyImmediate(node.gameObject, true);
                removed++;
            }

            return removed;
        }

        // ---------------------------------------------------------------------- poz düğümleri

        /// <summary>
        /// Adı <see cref="ItemGripPoses"/>'den gelen poz düğümü; <b>poz verisi taşımasa da</b> döner.
        /// <para>⚠️ <see cref="ItemGripPoses.Find"/> kullanılmaz: o, tüketici tarafı için
        /// <c>UsesHandPose</c> false olan düğümü "yok" sayar — kaydın ise yarım kalmış bir düğümü
        /// <b>bulup</b> üzerine yazması gerekir, yoksa her koşuda ikinci bir düğüm üretilirdi.</para>
        /// </summary>
        private static HandGrabPose FindPoseNode(Transform itemRoot, GripSocketKind kind, bool rightHand)
        {
            if (itemRoot == null)
            {
                return null;
            }

            Transform root = itemRoot.Find(ItemGripPoses.RootNodeName);
            if (root == null)
            {
                return null;
            }

            Transform node = root.Find(ItemGripPoses.NodeName(kind, rightHand));
            return node == null ? null : node.GetComponent<HandGrabPose>();
        }

        /// <summary>
        /// Poz düğümünü (ve gerekiyorsa <c>GripPoses</c> kökünü) üretir; varsa yenisini üretmez.
        /// <para>
        /// ⚠️ <b>Üretilen <see cref="HandGrabPose"/> hiçbir <c>HandGrabInteractable</c>'ın poz
        /// listesine EKLENMEZ.</b> Liste dolduğu anda ISDK kavrama adaylığını poz skoruna göre
        /// hesaplamaya başlar ve bugünkü kavrama hissi (collider mesafesi) sessizce değişirdi. Bu
        /// düğümler saf VERİDİR; onları yalnız <c>HandGripPoser</c> okur.
        /// </para>
        /// </summary>
        private static HandGrabPose EnsurePoseNode(Transform itemRoot, GripSocketKind kind, bool rightHand)
        {
            HandGrabPose existing = FindPoseNode(itemRoot, kind, rightHand);
            if (existing != null)
            {
                return existing;
            }

            Transform poseRoot = EnsurePoseRoot(itemRoot);

            HandGrabPose node = HandGrabUtils.CreateHandGrabPose(poseRoot, itemRoot);
            node.gameObject.name = ItemGripPoses.NodeName(kind, rightHand);
            node.InjectOptionalHandPose(
                CreateDefaultHandPose(rightHand ? Handedness.Right : Handedness.Left));
            return node;
        }

        /// <summary>
        /// ⚠️ Kök her koşuda kimliğe sabitlenir: poz düğümlerinin yerel pozu silah köküne göre
        /// yazılıyor, araya kaymış bir <c>GripPoses</c> transformu o ölçüyü sessizce kaydırırdı.
        /// </summary>
        private static Transform EnsurePoseRoot(Transform itemRoot)
        {
            Transform root = itemRoot.Find(ItemGripPoses.RootNodeName);
            if (root == null)
            {
                var go = new GameObject(ItemGripPoses.RootNodeName);
                go.transform.SetParent(itemRoot, false);
                root = go.transform;
            }

            root.localPosition = Vector3.zero;
            root.localRotation = Quaternion.identity;
            root.localScale = Vector3.one;
            return root;
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

        // ------------------------------------------------------------------------- el dalı ölçümü

        /// <summary>
        /// ISDK'nın hangi el dalında derlendiğini <b>ölçer</b> (bir kez; sonuç alana yazılır).
        /// <para>
        /// Yöntem: <see cref="HandGrabPose.HandPose"/> public getter'ı OpenXR dalında
        /// <c>_targetHandPose</c>'u, OVR dalında <c>_handPose</c>'u döndürüyor. İkisi ayrı nesne
        /// olduğu için getter'ın döndürdüğü referansı <c>_targetHandPose</c> ile karşılaştırmak dalı
        /// kesin söyler.
        /// </para>
        /// <para>
        /// ⚠️ <b>Neden ölçüm, neden <c>#if</c> değil:</b> <c>ISDK_OPENXR_HAND</c> paketin kendi
        /// asmdef'inde <c>versionDefines</c> ile üretiliyor ve <b>yalnız o assembly'de</b> tanımlı.
        /// Bizim derlememizde her zaman false çıkardı, yani <c>#if</c> ile yazılan her satır sessizce
        /// yanlış dalı seçerdi. Aynı sebeple define'ı kendi asmdef'imize kopyalamak da yasak: ISDK'nın
        /// iç ayrıntısı ikinci bir doğruluk kaynağı olur ve paket değişince kimse fark etmeden sapar.
        /// </para>
        /// </summary>
        private static void TryMeasureHandBranch(Transform itemRoot)
        {
            if (_openXrHandBranch.HasValue || TargetHandPoseField == null)
            {
                if (TargetHandPoseField == null)
                {
                    // Alan hiç yoksa paket sözleşmesi değişmiş demektir; ISDK'nın gittiği yön
                    // OpenXR, yanlışsa sağlayıcı yükleme adımı zaten ötekine düşer.
                    _openXrHandBranch = true;
                }

                return;
            }

            if (itemRoot == null)
            {
                return;
            }

            // ⚠️ `??` KULLANILMAZ: Unity'nin yok edilmiş nesne için özelleştirdiği `==` operatörünü
            // atlar ve ölü bir referansı "dolu" sayardı.
            HandGrabPose probe = FindPoseNode(itemRoot, GripSocketKind.Primary, true);
            if (probe == null)
            {
                probe = FindPoseNode(itemRoot, GripSocketKind.Primary, false);
            }

            if (probe == null)
            {
                return;
            }

            HandPose live = probe.HandPose;
            if (live == null)
            {
                return; // sonda geçersiz — ÖLÇÜLMEDİ sayılır, bir sonraki çağrıda tekrar denenir
            }

            _openXrHandBranch = ReferenceEquals(TargetHandPoseField.GetValue(probe), live);
        }

        /// <summary>
        /// Ölçülen el dalına ait model sağlayıcısını yükler; o yüklenemezse ötekine düşer.
        /// <para>⚠️ Sağlayıcı <b>dala aittir</b>: OpenXR iskeletiyle OVR modelini kurmak eli bozuk
        /// bir duruşa sokar (eklem sayısı ve sırası aynı değil). Ölçüm yapılamadıysa OpenXR
        /// varsayılır — paketin <c>versionDefines</c>'ı bu dalı her Unity sürümünde açıyor.</para>
        /// </summary>
        private static bool TryGetGhostProvider(Transform itemRoot, out HandGhostProvider provider)
        {
            TryMeasureHandBranch(itemRoot);

            string wanted = _openXrHandBranch.GetValueOrDefault(true)
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
        /// Hangi sağlayıcının kullanıldığını (ve dalın ölçülüp ölçülemediğini) tek satırda yazar.
        /// <para>⚠️ Bu satır süs değil <b>teşhistir</b>: yanlış dalın sağlayıcısı yüklendiğinde el
        /// yine kurulur, yalnız iskeleti tutmaz — belirtisi "el garip duruyor" olur ve sebebi hiçbir
        /// yerde görünmezdi.</para>
        /// </summary>
        private void DrawGhostSourceSection()
        {
            if (TargetHandPoseField == null)
            {
                EditorGUILayout.HelpBox(
                    "ISDK'nın HandGrabPose sınıfında '_targetHandPose' alanı yok — paket sözleşmesi " +
                    "değişmiş olabilir. El dalı ölçülemedi, OpenXR varsayıldı.",
                    MessageType.Warning);
            }

            string asset = string.IsNullOrEmpty(_ghostProviderPath)
                ? "(henüz yüklenmedi)"
                : System.IO.Path.GetFileName(_ghostProviderPath);

            string measured;
            if (TargetHandPoseField == null || !_openXrHandBranch.HasValue)
            {
                measured = "OpenXR (ölçülemedi, varsayıldı)";
            }
            else
            {
                measured = _openXrHandBranch.Value ? "OpenXR (ölçüldü)" : "OVR (ölçüldü)";
            }

            EditorGUILayout.HelpBox($"El dalı: {measured} · el modeli: {asset}", MessageType.None);
        }

        // ---------------------------------------------------------------------- parça arama

        /// <summary>
        /// Silahın alt ağacında, adında anahtarlardan biri geçen ilk <see cref="Renderer"/>.
        /// Anahtarlar <b>sırayla</b> denenir, ilk tutan kazanır (spesifik olan listede önce durur).
        /// <para>Tek tüketicisi <see cref="ResolveStartPose"/>: elin nereye konarak açılacağını
        /// çözerken kabza/ön kabza parçasının kabaca ortası son çare olarak kullanılıyor.</para>
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
        /// Parça aramasına girmemesi gereken dallar: kavrama pozu düğümleri, <b>el modelleri</b>,
        /// kavrama çerçevesi.
        /// <para>⚠️ Bu eleme olmadan araç <b>kendi kurduğu şeyi</b> kabza sanabilir — el modelinin ve
        /// çerçeve prefabının altında da Renderer var ve adları aranan anahtarlarla çakışabiliyor
        /// (yeni el o zaman kendi eski elinin üstünde açılırdı).</para>
        /// </summary>
        private static bool IsPartSearchNoise(Transform node)
        {
            if (node.name == ItemGripPoses.RootNodeName || node.name == ItemHandRig.RootNodeName ||
                node.name.StartsWith(HAND_ROOT_PREFIX))
            {
                return true;
            }

            return node.GetComponent<WeaponFrame>() != null;
        }

        // -------------------------------------------------------------------- varsayılan poz

        /// <summary>
        /// İlgili elin <b>bind</b> iskeletinden kurulmuş poz (ISDK'nın varsayılan duruşu).
        /// <para>⚠️ <c>new HandPose(handedness)</c> tek başına yetmez: o kurucu eklem dizisini
        /// doldurmaz, dizideki quaternion'lar sıfır kalır ve puppet eli çizilemez bir duruşa
        /// sokar (ekranda hiç el yoktur, hata da yoktur).</para>
        /// <para>
        /// <see cref="HandSkeleton"/> ve <see cref="FingersMetadata"/> burada <b>dala bağlı
        /// değildir</b>: ISDK iki iskelet tablosunu da yazmış ama namespace'i dala göre anahtarlıyor —
        /// <c>Oculus.Interaction.Input.HandSkeleton</c> adı hangi dal derlendiyse onun tablosuna
        /// çözülür. Yani burada ek bir seçim adımı YOK; el sağlayıcısının aksine (o bir asset
        /// yoludur, ad çözümlemesi onu bulamaz).
        /// </para>
        /// </summary>
        internal static HandPose CreateDefaultHandPose(Handedness handedness)
        {
            var pose = new HandPose(handedness);
            IReadOnlyHandSkeletonJointList joints = handedness == Handedness.Left
                ? HandSkeleton.DefaultLeftSkeleton
                : HandSkeleton.DefaultRightSkeleton;

            Quaternion[] rotations = pose.JointRotations;
            for (int i = 0; i < FingersMetadata.HAND_JOINT_IDS.Length && i < rotations.Length; i++)
            {
                rotations[i] = joints[(int)FingersMetadata.HAND_JOINT_IDS[i]].pose.rotation;
            }

            return pose;
        }
    }
}
