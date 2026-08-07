using System.Collections.Generic;
using System.Reflection;
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
    /// duracağını <b>gözlük takmadan</b> ayarlama tezgâhı: sahneye silahın sabit bir kopyası ve
    /// ayrı ayrı taşınabilen <b>iki el</b> kurulur; sen elleri kabzalara oturtursun, <b>Kaydet</b>
    /// ellerin silaha göre duruşunu tanıma yazar. Oyunda silah ele göre gelir.
    /// <para>
    /// ⚠️ <b>Sabit olan SİLAHTIR</b> (dünya orijininde, dönüşsüz): kaydedilen ölçü "silah ele göre
    /// nerede" olduğu için, silah orijindeyken elin transformu doğrudan o ölçüdür. Her silahın
    /// kabzası farklı açıdan tutulur ve kabza–tetik mesafesi de aynı değildir; iki elin ayrı ayrı
    /// ve aynı referansa göre ayarlanabilmesi bu yüzden gerekiyor.
    /// </para>
    /// <para>
    /// ⚠️ <b>El, kumanda anchor'ı çerçevesinde durur — bu aracın var olma sebebidir.</b> ISDK el
    /// modelinin kök transformu <b>bilek</b> çerçevesindedir, oyunun kavrama alanlarını okuduğu
    /// çerçeve ise <b>kumanda anchor'ıdır</b> (<see cref="HandGripPivot"/>) ve ikisi arasında sabit
    /// bir dönüş vardır. Ham bir el modelini gözle kabzaya oturtmak o dönüşü tanıma yazar ve silah
    /// oyunda o kadar dönük çıkar (yaşanan belirti: 90° roll). Burada el düğümünün altındaki model
    /// çeviriyi <see cref="HandGripConvention.Correction"/> ile <b>bir kez</b> yiyor; kullanıcının
    /// sürüklediği düğüm zaten anchor çerçevesindedir, yani hata yapısal olarak imkânsız.
    /// </para>
    /// <para>
    /// <b>Aracın var olma sebebi geri besleme süresidir:</b> kavrama ofseti tek doğru değeri olan
    /// bir sayı değil, "avuç kabzaya değiyor mu / işaret parmağı tetiğe ulaşıyor mu" sorusunun
    /// cevabıdır. O soruyu APK build'i + gözlük turuyla sormak her denemeyi dakikalara çıkarıyordu.
    /// </para>
    /// <para>
    /// ⚠️ <b>Tezgâh sahneye KAYDEDİLMEZ</b> (<see cref="HideFlags.DontSave"/>): açık unutulup
    /// commit'lenen bir el/silah kopyası, arenada havada duran bir silah demekti.
    /// </para>
    /// <para>
    /// ⚠️ <b>Dialog YOK</b> (<c>WeaponKitBuilder</c> ile aynı gerekçe): modal dialog Unity ana
    /// thread'ini kilitler ve CLI/pipeline üzerinden çalıştırıldığında komut timeout verir. Sonuç
    /// <see cref="Debug.Log"/> ile bildirilir.
    /// </para>
    /// </summary>
    internal sealed class GripPoseStudio : EditorWindow
    {
        private const string LOG = "[GripPoseStudio]";

        /// <summary>
        /// Tezgâhın kök düğümü. ⚠️ Ad <b>anahtardır</b>: tezgâh pencerede değil SAHNEDE yaşıyor
        /// (domain reload, pencere kapanması, sahne değişimi ona dokunmasın diye) ve her seferinde
        /// bu adla bulunur.
        /// </summary>
        private const string BENCH_ROOT_NAME = "[VA Kavrama Tezgahı]";

        /// <summary>Ana el — kullanıcı bunu taşır/çevirir. Transformu <b>avuç (kumanda anchor'ı)
        /// çerçevesidir</b>; el modeli altında, çeviriyi yemiş hâlde durur.</summary>
        private const string PALM_PRIMARY_NAME = "El_Primary";

        /// <summary>Ön kabza eli — ana elle aynı, ayrı ayarlanır.</summary>
        private const string PALM_SECONDARY_NAME = "El_Secondary";

        /// <summary>El düğümünün altındaki ISDK el modeli.
        /// ⚠️ Adı <c>Model</c> OLMAZ: silahın kendi <c>Model</c> düğümüyle çakışır ve ölçüm
        /// taraması kabza/tetik ararken silahın gövdesini elemeye başlardı.</summary>
        private const string HAND_NODE_NAME = "ElModeli";

        /// <summary>Tezgâhtaki silah kopyası — <b>dünya orijininde, dönüşsüz</b> ve KİLİTLİ.
        /// ⚠️ Prefab ÖRNEĞİ değil düz kopyadır: tezgâh atılacak bir maket, prefaba yazan tek yol
        /// <see cref="SaveBench"/>'tir.</summary>
        private const string WEAPON_NODE_NAME = "Silah";

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

        /// <summary>Tetik düğümünün ad anahtarı.</summary>
        private static readonly string[] TRIGGER_KEYS = { "trigger" };

        /// <summary>Bir el modelinden türetilen ölçüm noktaları.</summary>
        private sealed class HandMeasure
        {
            public GripSocketKind Kind;
            public Transform Node;

            /// <summary>Avucun kabzaya değen noktası (orta parmak boğumu); yoksa bilek kökü.</summary>
            public Transform Palm;

            /// <summary>
            /// İşaret parmağının ucu; rig'de yoksa <c>null</c> (ölçü sessizce atlanır).
            /// <para>⚠️ Tetiği çeken parmak <b>işaret</b> parmağıdır — konuşurken "baş parmak tetiğe
            /// değiyor mu" denmesi ölçünün baş parmağa bağlanmasını gerektirmez.</para>
            /// </summary>
            public Transform IndexTip;
        }

        /// <summary>Üstünde çalışılan silah PREFABI (asset). Tezgâhtaki kopya bundan üretilir.</summary>
        [SerializeField] private GameObject _prefab;

        private readonly List<HandMeasure> _measures = new List<HandMeasure>();
        private bool _measuresDirty = true;

        /// <summary>
        /// Anahtar kümesi → tezgâhtaki silahın parçası (bulunamadıysa <c>null</c> olarak da kaydedilir).
        /// <para>⚠️ Önbellek ŞART: <c>duringSceneGui</c> her karede koşuyor, taramasız hâlde her
        /// kare silahın tüm alt ağacını üç kez gezerdi. Tezgâh değişiminde temizlenir.</para>
        /// </summary>
        private readonly Dictionary<string, Renderer> _partCache = new Dictionary<string, Renderer>();

        private HandGhostProvider _ghostProvider;

        /// <summary>Yüklü sağlayıcının yolu — dal ölçümü değişince yeniden yükleneceğini bilmek için.</summary>
        private string _ghostProviderPath;

        /// <summary>
        /// Sağlayıcı hiç bulunamadığında uyarı bir kez basılsın diye.
        /// <para>⚠️ Bu yol <c>OnGUI</c>'den her karede geçebiliyor: bayraksız hâlde eksik bir asset
        /// konsolu saniyede onlarca satırla boğardı.</para>
        /// </summary>
        private bool _ghostProviderWarned;

        /// <summary>
        /// ISDK OpenXR dalında mı derlendi — <c>null</c> ise <b>henüz ölçülemedi</b>.
        /// <para>⚠️ Ölçülemeyen sonuç önbelleğe ALINMAZ: sonda olarak kullanılan düğümün
        /// <c>_usesHandPose</c>'u kapalıysa <see cref="HandGrabPose.HandPose"/> her iki dalda da
        /// <c>null</c> döner ve o an alınan "cevap" dala değil düğüme ait olurdu.</para>
        /// </summary>
        private bool? _openXrHandBranch;

        private HandPose _defaultLeftPose;
        private HandPose _defaultRightPose;
        private GUIStyle _labelStyle;

        // --------------------------------------------------------------------------- pencere

        // Öncelik 24: 23'ü NetItemIdGuard'ın "Rebuild Net Item Catalog"u kullanıyor. Aynı sayı
        // Unity'de hata üretmez ama iki öğenin sırası belirsizleşir — menü sırası okuyanın kafasında
        // "hangisi önce" sorusu doğurmasın.
        [MenuItem("Tools/VortexArena/Weapons/Kavrama Pozu Stüdyosu", false, 24)]
        private static void Open()
        {
            GripPoseStudio window = GetWindow<GripPoseStudio>();
            window.titleContent = new GUIContent("Kavrama Tezgâhı");
            window.minSize = new Vector2(340f, 340f);
            window.Show();
        }

        private void OnEnable()
        {
            SceneView.duringSceneGui += OnSceneGui;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            EditorSceneManager.sceneOpened += OnSceneOpened;
            _measuresDirty = true;
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGui;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorSceneManager.sceneOpened -= OnSceneOpened;
            SceneView.RepaintAll();
        }

        /// <summary>
        /// ⚠️ Play'e girerken tezgâh KAPATILIR. <see cref="HideFlags.DontSave"/> objeler sahne
        /// yeniden yüklendiğinde ölmüyor: kapatılmasaydı çalışan oyunun ortasında bir el ve bir
        /// silah kopyası dururdu (ve o kopyanın <c>Weapon</c>'ı gerçek silah gibi davranırdı).
        /// </summary>
        private void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.ExitingEditMode)
            {
                CloseBench(silent: true);
            }

            _measuresDirty = true;
            Repaint();
        }

        /// <summary>Yeni sahne = tezgâhın bağlamı gitti; kalıntı bırakmamak için kapatılır.</summary>
        private void OnSceneOpened(Scene scene, OpenSceneMode mode)
        {
            CloseBench(silent: true);
            _measuresDirty = true;
            Repaint();
        }

        /// <summary>
        /// Proje penceresinde bir <c>WPN_*</c> prefabı seçilince hedef kendiliğinden dolar.
        /// <para>⚠️ Yalnız PREFAB ASSET'i kabul edilir: sahnedeki bir seçim (tezgâhın kendi silahı
        /// dahil) hedefi değiştirseydi, tezgâhta çalışırken hedef altından kayardı.</para>
        /// </summary>
        private void OnSelectionChange()
        {
            GameObject candidate = Selection.activeGameObject;
            if (candidate != null &&
                PrefabUtility.IsPartOfPrefabAsset(candidate) &&
                candidate.GetComponent<Weapon>() != null)
            {
                _prefab = candidate;
                _openXrHandBranch = null;
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

            _prefab = (GameObject)EditorGUILayout.ObjectField(
                "Silah prefabı", _prefab, typeof(GameObject), false);

            Transform bench = FindBenchRoot();

            if (bench == null)
            {
                DrawClosedBenchGui();
                return;
            }

            DrawOpenBenchGui(bench);
        }

        private void DrawClosedBenchGui()
        {
            WeaponDefinition definition = ResolveDefinition(_prefab);

            using (new EditorGUI.DisabledScope(definition == null))
            {
                if (GUILayout.Button("Tezgâhı Aç", GUILayout.Height(26f)))
                {
                    OpenBench();
                }
            }

            if (_prefab == null)
            {
                EditorGUILayout.HelpBox(
                    "Hedef yok: proje penceresinden bir WPN_* prefabı seç (ya da yukarıdaki alana sürükle).",
                    MessageType.Info);
                return;
            }

            if (definition == null)
            {
                EditorGUILayout.HelpBox(
                    "Bu prefabın Weapon bileşeni ya da tanımı (WeaponDefinition) yok — " +
                    "kaydedilecek asset bulunamaz.",
                    MessageType.Warning);
                return;
            }

            EditorGUILayout.HelpBox(
                "Tezgâh AÇIK sahnede kurulur ve sahneye kaydedilmez. Silah orijinde sabit durur; " +
                "sen iki eli ayrı ayrı kabzalara oturtursun. Oyunda silah ele göre gelir.",
                MessageType.None);

            DrawGhostSourceSection();
        }

        private void DrawOpenBenchGui(Transform bench)
        {
            Transform weapon = bench.Find(WEAPON_NODE_NAME);
            Transform palmPrimary = bench.Find(PALM_PRIMARY_NAME);
            Transform palmSecondary = bench.Find(PALM_SECONDARY_NAME);

            if (weapon == null || palmPrimary == null)
            {
                EditorGUILayout.HelpBox(
                    "Tezgâh bozuk (silah ya da avuç düğümü yok). Kapatıp yeniden aç.",
                    MessageType.Error);

                if (GUILayout.Button("Tezgâhı Kapat"))
                {
                    CloseBench(silent: false);
                }

                return;
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Eli seç ve Scene'de sürükle", EditorStyles.boldLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Ana el"))
                {
                    Selection.activeGameObject = palmPrimary.gameObject;
                }

                using (new EditorGUI.DisabledScope(palmSecondary == null))
                {
                    if (GUILayout.Button("Ön kabza eli") && palmSecondary != null)
                    {
                        Selection.activeGameObject = palmSecondary.gameObject;
                    }
                }
            }

            EditorGUILayout.LabelField(
                "Silah orijinde SABİT ve tıklanamaz. Parmakları bükmek için Hierarchy'den " +
                $"{PALM_PRIMARY_NAME}/{HAND_NODE_NAME}/…/XRHand_* kemiklerini seç.",
                EditorStyles.miniLabel);

            EditorGUILayout.Space();
            DrawLiveValues(weapon, palmPrimary, palmSecondary);

            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Kaydet", GUILayout.Height(26f)))
                {
                    SaveBench(bench);
                }

                if (GUILayout.Button("Tezgâhı Kapat", GUILayout.Height(26f)))
                {
                    CloseBench(silent: false);
                }
            }

            DrawMissingPartsSection(ResolveDefinition(_prefab));
            DrawGhostSourceSection();

            EditorGUILayout.HelpBox(
                "Akış: Tezgâhı Aç → elleri kabzalara oturt/çevir → parmakları bük → Kaydet. " +
                "Yazan tek düğme Kaydet'tir; yukarıdaki sayılar ellerin silaha göre o andaki " +
                "duruşudur ve oyunda silah ele göre gelir.",
                MessageType.None);
        }

        /// <summary>
        /// Kaydedilecek sayıları <b>canlı</b> gösterir.
        /// <para>⚠️ Salt okunurdur ve öyle kalır: bu sayıların düzenlenebilir olması, aynı kavramayı
        /// iki yerde (bir alanda ve bir transformda) tarif etmek olurdu — ikisi zamanla sessizce
        /// sapar ve belirtisi "silah bazı yerlerde doğru duruyor" olurdu.</para>
        /// </summary>
        private void DrawLiveValues(Transform weapon, Transform palmPrimary, Transform palmSecondary)
        {
            EditorGUILayout.LabelField("Kaydedilecek (salt okunur)", EditorStyles.boldLabel);

            ItemHandGripBake.FromWrist(weapon, palmPrimary, GripSocketKind.Primary,
                out Vector3 primaryPosition, out Vector3 primaryEuler);

            EditorGUILayout.LabelField("primaryGripPosition", Format(primaryPosition));
            EditorGUILayout.LabelField("primaryGripEuler", Format(primaryEuler));

            if (palmSecondary == null)
            {
                return;
            }

            ItemHandGripBake.FromWrist(weapon, palmSecondary, GripSocketKind.Secondary,
                out Vector3 secondaryPosition, out Vector3 secondaryEuler);

            EditorGUILayout.LabelField("secondaryGripPosition", Format(secondaryPosition));
            EditorGUILayout.LabelField("secondaryGripEuler", Format(secondaryEuler));
        }

        private static string Format(Vector3 value)
        {
            return $"({value.x:0.####}, {value.y:0.####}, {value.z:0.####})";
        }

        // ---------------------------------------------------------------------------- tezgâh

        private static Transform FindBenchRoot()
        {
            for (int s = 0; s < SceneManager.sceneCount; s++)
            {
                Scene scene = SceneManager.GetSceneAt(s);
                if (!scene.isLoaded)
                {
                    continue;
                }

                GameObject[] roots = scene.GetRootGameObjects();
                for (int i = 0; i < roots.Length; i++)
                {
                    if (roots[i] != null && roots[i].name == BENCH_ROOT_NAME)
                    {
                        return roots[i].transform;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Tezgâhı kurar: dünya orijininde <b>sabit</b> bir silah kopyası ve onun kabzalarına
        /// oturmuş, ayrı ayrı taşınabilen iki el.
        /// <para>
        /// ⚠️ <b>Silahın sabit olması bir tercih değil, ölçünün tanımı.</b> Kaydedilen şey "silah
        /// ELE göre nerede duruyor"dur; silah orijinde ve dönüşsüzken elin transformu doğrudan o
        /// ölçüdür. Ayrıca her silahın kabzası farklı açıdan tutulur (pompalı alttan, otomatik
        /// tüfek yandan) ve iki el birbirinden bağımsız ayarlanmak zorunda — sabit referans silah
        /// olunca iki el de aynı referansa göre okunur.
        /// </para>
        /// <para>
        /// ⚠️ Eller <see cref="ItemHandGripBake.ToWristLocal"/> ile, yani <b>kaydın birebir
        /// tersiyle</b> yerleştirilir: "aç → hiç dokunma → kaydet" tanımdaki sayıyı DEĞİŞTİRMEZ. O
        /// kimlik bozulursa uzay yönlerinden biri ters demektir ve bakılacak tek yer
        /// <see cref="ItemHandGripBake"/>'tir.
        /// </para>
        /// </summary>
        private void OpenBench()
        {
            WeaponDefinition definition = ResolveDefinition(_prefab);
            if (definition == null)
            {
                return;
            }

            CloseBench(silent: true);

            var root = new GameObject(BENCH_ROOT_NAME);
            root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

            GameObject weapon = Instantiate(_prefab);
            weapon.name = WEAPON_NODE_NAME;
            weapon.transform.SetParent(root.transform, false);

            // ⚠️ Silah her koşuda orijine ve kimlik dönüşe sabitlenir (ölçeğine dokunulmaz):
            // prefabın kökü sıfırda olmayabilir ve tezgâhın referansı "silah = 0,0,0" olmak
            // zorunda — ellerin kaydedilen ofsetleri ona göre okunuyor.
            weapon.transform.localPosition = Vector3.zero;
            weapon.transform.localRotation = Quaternion.identity;

            AddPalm(root.transform, weapon.transform, definition, GripSocketKind.Primary);

            if (definition.IsTwoHanded)
            {
                AddPalm(root.transform, weapon.transform, definition, GripSocketKind.Secondary);
            }

            // ⚠️ Silahın tıklanması KAPATILIR: elleri sürüklerken silahın gövdesine tıklamak çok
            // kolay ve onu kazara oynatmak sessiz bir hatadır (ekranda her şey doğru görünür,
            // yazılan sayı yanlış olur). Kilit yalnız tıklamaya karşıdır — Hierarchy'den yine
            // seçilir.
            DisablePicking(weapon.transform);

            MarkDontSave(root);

            Transform primary = root.transform.Find(PALM_PRIMARY_NAME);
            Selection.activeGameObject = primary != null ? primary.gameObject : weapon;
            _partCache.Clear();
            _measuresDirty = true;
            FrameBench(weapon.transform);
            SceneView.RepaintAll();
            Repaint();

            Debug.Log($"{LOG} '{_prefab.name}' için tezgâh açıldı. Silah orijinde SABİT; elleri " +
                      "kabzaya taşı/çevir, parmakları bük, sonra Kaydet'e bas. Tezgâh sahneye " +
                      "kaydedilmez.", weapon);
        }

        /// <summary>
        /// Bir kavrama noktasının elini kurar ve <b>mevcut tanımdan</b> konumlandırır.
        /// <para>⚠️ Yerleştirme <see cref="ItemHandGripBake.ToWristLocal"/> ile yapılır, yani
        /// kaydın birebir tersiyle — "tezgâhı aç, hiç dokunma, kaydet" kimliği buradan geliyor.
        /// İki kavrama noktasının uzayı ters olduğu için (ana el "el → eşya", ikincil "eşya → el")
        /// bileşimi elle yazmak yasak; asimetrinin tek uygulaması o sınıftadır.</para>
        /// </summary>
        private void AddPalm(Transform benchRoot, Transform weapon, WeaponDefinition definition,
            GripSocketKind kind)
        {
            var palm = new GameObject(
                kind == GripSocketKind.Primary ? PALM_PRIMARY_NAME : PALM_SECONDARY_NAME);
            palm.transform.SetParent(benchRoot, false);

            ItemHandGripBake.ToWristLocal(definition, kind,
                out Vector3 localPosition, out Quaternion localRotation);
            palm.transform.SetPositionAndRotation(
                weapon.position + weapon.rotation * localPosition,
                weapon.rotation * localRotation);

            AddHand(palm.transform, kind);

            // El modelinin kendisi tıklanamaz: kullanıcının sürüklemesi gereken şey EL DÜĞÜMÜ
            // (avuç çerçevesi), mesh'in altındaki bir parmak kemiği değil. Parmaklar Hierarchy'den
            // seçilip bükülür.
            DisablePicking(palm.transform.Find(HAND_NODE_NAME));
        }

        /// <summary>
        /// Tezgâhı siler. <paramref name="silent"/> otomatik kapanışlarda (play kipi, sahne
        /// değişimi) log basmamak içindir.
        /// </summary>
        private void CloseBench(bool silent)
        {
            Transform bench = FindBenchRoot();
            if (bench == null)
            {
                return;
            }

            DestroyImmediate(bench.gameObject);
            _partCache.Clear();
            _measures.Clear();
            _measuresDirty = true;
            SceneView.RepaintAll();
            Repaint();

            if (!silent)
            {
                Debug.Log($"{LOG} Tezgâh kapatıldı (kaydedilmemiş değişiklikler atıldı).");
            }
        }

        private static void FrameBench(Transform weapon)
        {
            SceneView view = SceneView.lastActiveSceneView;
            if (view == null)
            {
                return;
            }

            view.Frame(new Bounds(weapon.position, Vector3.one * 0.6f), false);
        }

        /// <summary>
        /// ⚠️ <see cref="HideFlags.DontSave"/> tüm alt ağaca yazılır: tezgâh açıkken sahne
        /// kaydedilirse tek bir el/silah kopyası bile dosyaya girmesin. Bayrak GameObject
        /// başınadır, kökte olması yetmez.
        /// </summary>
        private static void MarkDontSave(GameObject root)
        {
            Transform[] all = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                all[i].gameObject.hideFlags = HideFlags.DontSave;
            }
        }

        private static void DisablePicking(Transform node)
        {
            if (node != null)
            {
                SceneVisibilityManager.instance.DisablePicking(node.gameObject, true);
            }
        }

        // ------------------------------------------------------------------------------- el

        /// <summary>
        /// Avuç düğümünün altına el modelini kurar.
        /// <para>
        /// ⚠️ <b>Elin yerel dönüşü ELLE AYARLANMAZ, TÜRETİLİR</b>
        /// (<see cref="HandGripConvention.Correction"/>): avuç düğümü kumanda anchor'ı çerçevesinde,
        /// el modelinin kökü ise ISDK bilek çerçevesindedir. Aradaki dönüş, modelin KENDİ bind
        /// pozundan ölçülür (<see cref="HandGripConvention.TryMeasureBoneBasis"/>) — sabit olarak
        /// yazılsaydı ISDK el modeli değiştiğinde sessizce yanlış kalırdı.
        /// </para>
        /// <para>⚠️ Ölçüm <b>parmaklar poza sokulmadan ÖNCE</b> yapılır: bükülmüş bir elden ölçülen
        /// baz o duruşu içerir ve düzeltme kalıcı olarak yanlış çıkar.</para>
        /// <para>⚠️ Yerel ölçek 1'e sabitlenir: el düğümü tezgâh kökünün (ölçeksiz) altındadır,
        /// silah ise 0.8. El silahın altında kurulsaydı 0.8'e inerdi ve "avuç kabzayı sarıyor mu"
        /// sorusu %25 yanlış bir orandan cevaplanırdı — aracın tek işi o soruyu doğru
        /// cevaplatmak.</para>
        /// </summary>
        private void AddHand(Transform palm, GripSocketKind kind)
        {
            if (!TryGetGhostProvider(out HandGhostProvider provider))
            {
                return;
            }

            bool rightHand = ItemHandRig.AuthoredHandIsRight;
            Handedness handedness = rightHand ? Handedness.Right : Handedness.Left;

            HandGhost prototype = provider.GetHand(handedness);
            if (prototype == null)
            {
                Debug.LogWarning($"{LOG} El sağlayıcısında {handedness} el yok — el kurulamadı.");
                return;
            }

            HandGhost ghost = Instantiate(prototype, palm);
            ghost.gameObject.name = HAND_NODE_NAME;
            ghost.gameObject.hideFlags = HideFlags.None;

            // ⚠️ Yerel konum SIFIRDIR ve <c>HandGripPivot.PalmOffset</c> buraya EKLENMEZ: avuç
            // düğümü, çözücünün aldığı `primaryPalm` pozunun ta kendisi ve o poz zaten
            // `anchor + anchorRot * PalmOffset`, yani bileğin bulunduğu nokta
            // (HandGripPivot.Resolve). Ofseti bir kez daha uygulamak onu iki kez saymak olurdu —
            // bugün değer sıfır olduğu için görünmez, ölçülüp doldurulduğu gün el silahın önüne
            // kayardı ve sebebi görünmezdi.
            Transform node = ghost.transform;
            node.localScale = Vector3.one;
            node.localPosition = Vector3.zero;
            node.localRotation = ResolveWristCorrection(ghost, rightHand);

            // ⚠️ Prefabtaki poz KOPYALANIR, doğrudan verilmez: ISDK'nın puppet'ı verilen pozu
            // yerinde değiştirebiliyor ve buradaki kaynak ASSET'in kendisi (tezgâhın kopyası değil).
            // Kopyalamadan geçirmek, tezgâhı açmanın prefabı sessizce kirletmesi demek olurdu.
            HandGrabPose existing = FindPoseNode(_prefab.transform, kind, rightHand);
            HandPose source = existing != null ? existing.HandPose : null;
            HandPose startPose = source != null ? new HandPose(source) : DefaultHandPose(handedness);

            if (startPose != null)
            {
                ghost.SetPose(startPose, new Pose(node.position, node.rotation));
            }
        }

        /// <summary>
        /// Avuç (kumanda anchor'ı) çerçevesinden ISDK bilek çerçevesine dönüş.
        /// <para>Ölçülemezse kimlik döner ve <b>açıkça</b> uyarılır: sessizce kimlik bırakmak, elin
        /// yanlış yönde durmasına ve kullanıcının onu gözle "düzeltmesine" yol açardı — bu aracın
        /// var olma sebebi olan hatanın ta kendisi.</para>
        /// </summary>
        private static Quaternion ResolveWristCorrection(HandGhost ghost, bool rightHand)
        {
            HandPuppet puppet = ghost.GetComponent<HandPuppet>();
            Transform middle = FindJoint(puppet, HandJointId.HandMiddle1);
            Transform thumb = FindJoint(puppet, HandJointId.HandThumb1);

            if (HandGripConvention.TryMeasureBoneBasis(
                    ghost.transform, middle, thumb, rightHand, out Quaternion basis))
            {
                return HandGripConvention.Correction(rightHand, basis);
            }

            Debug.LogWarning($"{LOG} El modelinin anatomik bazı ölçülemedi (orta/baş parmak boğumu " +
                             "bulunamadı) — el kumanda çerçevesine hizalanamadı, dönüşü kimlik " +
                             "bırakıldı. Bu hâlde kavrama YAZMA.");
            return Quaternion.identity;
        }

        private static Transform FindJoint(HandPuppet puppet, HandJointId id)
        {
            if (puppet == null || puppet.JointMaps == null)
            {
                return null;
            }

            List<HandJointMap> maps = puppet.JointMaps;
            for (int i = 0; i < maps.Count; i++)
            {
                if (maps[i] != null && maps[i].id == id)
                {
                    return maps[i].transform;
                }
            }

            return null;
        }

        // ----------------------------------------------------------------------------- kayıt

        /// <summary>
        /// Tezgâhtaki duruşu kalıcı veriye çevirir: silahın avuca göre transformu →
        /// <c>WD_*.asset</c>, elin silaha göre pozu + parmaklar → <c>GripPoses/Pose_*</c> (prefab).
        /// <para>
        /// ⚠️ Prefab <b>headless</b> yazılır (<see cref="PrefabUtility.LoadPrefabContents"/>):
        /// prefab kipini açıp kapatmak kullanıcının o an elindeki sahneyi/seçimi bozardı ve
        /// tezgâhın kendisi de bir sahne objesi olduğu için iki stage arasında kaybolurdu.
        /// </para>
        /// <para>
        /// ⚠️ Poz düğümünün yerel konumu <see cref="Transform.InverseTransformPoint"/> ile
        /// hesaplanır — <b>bilerek ölçekli</b>. ISDK'nın <c>HandGrabPose.RelativePose</c>'u da öyle
        /// (<c>PoseUtils.DeltaScaled</c>); "ofset metredir, ölçeklenmez" kuralı tanımın kavrama
        /// alanları içindir, ISDK'nın kendi sözleşmesi için değil. İki taraf aynı sözleşmede
        /// olmazsa el silahtan 1/0.8 kadar uzağa yapışır.
        /// </para>
        /// </summary>
        private void SaveBench(Transform bench)
        {
            WeaponDefinition definition = ResolveDefinition(_prefab);
            string path = AssetDatabase.GetAssetPath(_prefab);

            if (definition == null || string.IsNullOrEmpty(path))
            {
                Debug.LogError($"{LOG} Kaydedilemedi: silah prefabı ya da tanımı çözülemedi.");
                return;
            }

            Transform weapon = bench.Find(WEAPON_NODE_NAME);
            Transform palmPrimary = bench.Find(PALM_PRIMARY_NAME);
            Transform palmSecondary = bench.Find(PALM_SECONDARY_NAME);

            if (weapon == null || palmPrimary == null)
            {
                Debug.LogError($"{LOG} Kaydedilemedi: tezgâhta silah ya da avuç düğümü yok.");
                return;
            }

            // 1) Kavrama alanları → tanım.
            var so = new SerializedObject(definition);
            WriteGripFields(so, weapon, palmPrimary, GripSocketKind.Primary);
            if (palmSecondary != null)
            {
                WriteGripFields(so, weapon, palmSecondary, GripSocketKind.Secondary);
            }

            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(definition);

            // 2) Parmak pozları → prefab.
            bool rightHand = ItemHandRig.AuthoredHandIsRight;
            GameObject contents = PrefabUtility.LoadPrefabContents(path);
            int legacyHands = 0;

            try
            {
                WritePoseNode(contents.transform, weapon,
                    palmPrimary.Find(HAND_NODE_NAME), GripSocketKind.Primary, rightHand);

                if (palmSecondary != null)
                {
                    WritePoseNode(contents.transform, weapon,
                        palmSecondary.Find(HAND_NODE_NAME), GripSocketKind.Secondary, rightHand);
                }

                legacyHands = RemoveLegacyHandRig(contents.transform);
                PrefabUtility.SaveAsPrefabAsset(contents, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }

            AssetDatabase.SaveAssets();

            string legacyNote = legacyHands > 0
                ? $" Eski '{ItemHandRig.RootNodeName}' düğümü silindi (kavrama artık tezgâhta yazılıyor)."
                : string.Empty;

            Debug.Log($"{LOG} '{_prefab.name}' kavraması yazıldı: {definition.name} kavrama alanları + " +
                      $"{ItemGripPoses.RootNodeName} pozları (sağ + aynalanmış sol).{legacyNote} " +
                      "Tezgâh açık kalıyor — beğenmediysen düzelt ve tekrar Kaydet'e bas.", definition);
        }

        private static void WriteGripFields(SerializedObject so, Transform weapon, Transform palm,
            GripSocketKind kind)
        {
            ItemHandGripBake.FromWrist(weapon, palm, kind,
                out Vector3 gripPosition, out Vector3 gripEuler);

            so.FindProperty(kind == GripSocketKind.Primary
                ? "primaryGripPosition"
                : "secondaryGripPosition").vector3Value = gripPosition;
            so.FindProperty(kind == GripSocketKind.Primary
                ? "primaryGripEuler"
                : "secondaryGripEuler").vector3Value = gripEuler;
        }

        /// <summary>
        /// Bir kavrama noktasının poz düğümünü prefaba yazar ve karşı ele aynalar.
        /// <para>⚠️ Ayna matematiği ELLE YAZILMAZ: ISDK'nın kendi
        /// <see cref="HandGrabUtils.MirrorHandGrabPose"/>'u kullanılır. İki eli ayrı ayrı yazmak
        /// aynı kavramanın iki kez tarif edilmesi olurdu ve ikisi zamanla kaçınılmaz olarak
        /// saparadı.</para>
        /// </summary>
        private void WritePoseNode(Transform contentsRoot, Transform benchWeapon, Transform benchHand,
            GripSocketKind kind, bool rightHand)
        {
            if (benchHand == null)
            {
                Debug.LogWarning($"{LOG} {kind} eli tezgâhta yok — o kavramanın parmak pozu yazılmadı.");
                return;
            }

            HandPuppet puppet = benchHand.GetComponent<HandPuppet>();
            if (puppet == null || puppet.JointMaps == null)
            {
                Debug.LogError($"{LOG} {kind} elinde HandPuppet yok — parmak rotasyonları okunamadı.");
                return;
            }

            HandGrabPose node = EnsurePoseNode(contentsRoot, kind, rightHand);

            node.transform.localPosition = benchWeapon.InverseTransformPoint(benchHand.position);
            node.transform.localRotation = Quaternion.Inverse(benchWeapon.rotation) * benchHand.rotation;
            node.transform.localScale = Vector3.one;

            if (node.RelativeTo == null)
            {
                node.InjectRelativeTo(contentsRoot);
            }

            Handedness handedness = rightHand ? Handedness.Right : Handedness.Left;
            HandPose pose = node.HandPose ?? new HandPose(handedness);
            WriteJointRotations(puppet, pose);
            node.InjectOptionalHandPose(pose);

            HandGrabPose mirror = EnsurePoseNode(contentsRoot, kind, !rightHand);
            HandGrabUtils.MirrorHandGrabPose(node, mirror, contentsRoot);
        }

        /// <summary>
        /// Tezgâh yazma yolunu devraldıktan sonra prefabta kalan <c>Hands/Hand_*</c> ağacı ölü
        /// veridir ve silinir.
        /// <para>⚠️ Sessizce BIRAKILMAZ: o düğümler kavramanın ikinci (ve artık okunmayan) bir
        /// tarifidir; duran bir kopya, "hangisi geçerli" sorusunu her açanın kafasında yeniden
        /// doğurur. Runtime emniyeti (<see cref="ItemHandRig.HideAll"/>) eski prefablar için
        /// yerinde kalır.</para>
        /// </summary>
        private static int RemoveLegacyHandRig(Transform contentsRoot)
        {
            Transform node = contentsRoot.Find(ItemHandRig.RootNodeName);
            if (node == null)
            {
                return 0;
            }

            DestroyImmediate(node.gameObject, true);
            return 1;
        }

        /// <summary>
        /// El modelinin eklem transformlarını <see cref="HandPose.JointRotations"/> sırasına yazar.
        /// <para>⚠️ Sıra <see cref="FingersMetadata.HAND_JOINT_IDS"/>'dir ve eklem transformu
        /// <b>ada göre değil KİMLİĞE göre</b> bulunur (<see cref="HandJointMap.id"/>): el
        /// iskeletinin kemik adları ISDK'nın OpenXR ve OVR dallarında farklı, kimlikler aynıdır.</para>
        /// </summary>
        private static void WriteJointRotations(HandPuppet puppet, HandPose pose)
        {
            List<HandJointMap> maps = puppet.JointMaps;
            HandJointId[] ids = FingersMetadata.HAND_JOINT_IDS;

            for (int i = 0; i < ids.Length && i < pose.JointRotations.Length; i++)
            {
                HandJointId wanted = ids[i];
                HandJointMap map = maps.Find(m => m != null && m.id == wanted);
                if (map != null && map.transform != null)
                {
                    pose.JointRotations[i] = map.transform.localRotation;
                }
            }
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
        private HandGrabPose EnsurePoseNode(Transform itemRoot, GripSocketKind kind, bool rightHand)
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

        private static WeaponDefinition ResolveDefinition(GameObject prefab)
        {
            if (prefab == null)
            {
                return null;
            }

            var weapon = prefab.GetComponent<Weapon>();
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
        private void TryMeasureHandBranch()
        {
            if (_openXrHandBranch.HasValue || _prefab == null || TargetHandPoseField == null)
            {
                if (TargetHandPoseField == null)
                {
                    // Alan hiç yoksa paket sözleşmesi değişmiş demektir; ISDK'nın gittiği yön
                    // OpenXR, yanlışsa sağlayıcı yükleme adımı zaten ötekine düşer.
                    _openXrHandBranch = true;
                }

                return;
            }

            // ⚠️ `??` KULLANILMAZ: Unity'nin yok edilmiş nesne için özelleştirdiği `==` operatörünü
            // atlar ve ölü bir referansı "dolu" sayardı.
            HandGrabPose probe = FindPoseNode(_prefab.transform, GripSocketKind.Primary, true);
            if (probe == null)
            {
                probe = FindPoseNode(_prefab.transform, GripSocketKind.Primary, false);
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
        /// Ölçülen el dalına ait model sağlayıcısını yükler; o yükleneMEZse ötekine düşer.
        /// <para>⚠️ Sağlayıcı <b>dala aittir</b>: OpenXR iskeletiyle OVR modelini kurmak eli bozuk
        /// bir duruşa sokar (eklem sayısı ve sırası aynı değil). Ölçüm yapılamadıysa OpenXR
        /// varsayılır — paketin <c>versionDefines</c>'ı bu dalı her Unity sürümünde açıyor.</para>
        /// </summary>
        private bool TryGetGhostProvider(out HandGhostProvider provider)
        {
            TryMeasureHandBranch();

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

        // --------------------------------------------------------------------------- ölçüler

        private void OnSceneGui(SceneView sceneView)
        {
            if (EditorApplication.isPlaying)
            {
                return;
            }

            Transform bench = FindBenchRoot();
            if (bench == null)
            {
                return;
            }

            if (_measuresDirty)
            {
                RebuildMeasures(bench);
            }

            DrawPalmFrames(bench);
            DrawMeasurements(bench);
        }

        private void RebuildMeasures(Transform bench)
        {
            _measuresDirty = false;
            _measures.Clear();

            AddMeasure(bench, PALM_PRIMARY_NAME, GripSocketKind.Primary);
            AddMeasure(bench, PALM_SECONDARY_NAME, GripSocketKind.Secondary);
        }

        /// <summary>
        /// Bir el modelinin ölçüm noktalarını rig'inden çözer.
        /// <para>⚠️ Parmak <b>uçları</b> puppet'ın eklem tablosunda olmayabilir
        /// (<c>FingersMetadata.HAND_JOINT_IDS</c> uçları taşımaz) — bu yüzden son boğumun
        /// (<c>HandIndex3</c>) çocuğuna, o da yoksa ada göre aramaya düşülür. Hiçbiri tutmazsa ölçü
        /// sessizce çizilmez: eksik bir gösterge için hata basmak, aracın asıl işini gürültüye
        /// boğardı.</para>
        /// </summary>
        private void AddMeasure(Transform bench, string palmName, GripSocketKind kind)
        {
            Transform palm = bench.Find(palmName);
            Transform node = palm == null ? null : palm.Find(HAND_NODE_NAME);
            if (node == null)
            {
                return;
            }

            var measure = new HandMeasure { Kind = kind, Node = node, Palm = node };
            HandPuppet puppet = node.GetComponent<HandPuppet>();

            if (puppet != null && puppet.JointMaps != null)
            {
                Transform indexLast = null;
                for (int i = 0; i < puppet.JointMaps.Count; i++)
                {
                    HandJointMap map = puppet.JointMaps[i];
                    if (map == null || map.transform == null)
                    {
                        continue;
                    }

                    if (map.id == HandJointId.HandIndexTip)
                    {
                        measure.IndexTip = map.transform;
                    }
                    else if (map.id == HandJointId.HandIndex3)
                    {
                        indexLast = map.transform;
                    }
                    else if (map.id == HandJointId.HandMiddle1)
                    {
                        // ⚠️ "Avuç" olarak bilek kökü DEĞİL orta parmağın boğumu alınır: bilek
                        // kavrama noktasının tam üstündedir, yani ondan ölçülen mesafe tanımı gereği
                        // sıfıra yakındır ve hiçbir soruyu cevaplamaz. Kabzaya değen yer burasıdır.
                        measure.Palm = map.transform;
                    }
                }

                if (measure.IndexTip == null && indexLast != null)
                {
                    measure.IndexTip = indexLast.childCount > 0 ? indexLast.GetChild(0) : indexLast;
                }
            }

            if (measure.IndexTip == null)
            {
                measure.IndexTip = FindByName(node, "index", "null", "tip");
            }

            _measures.Add(measure);
        }

        /// <summary>Alt ağaçta adı <paramref name="required"/> ve seçeneklerden birini içeren ilk transform.</summary>
        private static Transform FindByName(Transform root, string required, string optionA, string optionB)
        {
            Transform[] all = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                string name = all[i].name.ToLowerInvariant();
                if (name.Contains(required) && (name.Contains(optionA) || name.Contains(optionB)))
                {
                    return all[i];
                }
            }

            return null;
        }

        /// <summary>
        /// El düğümlerinin çerçevesini çizer — <b>kaydedilecek sayının referansı budur</b>.
        /// <para>Mavi ok kumandanın ileri yönü, yeşil ok yukarısıdır; elin silaha göre duruşu
        /// doğrudan kavrama alanlarına yazılır.</para>
        /// </summary>
        private void DrawPalmFrames(Transform bench)
        {
            DrawPalmFrame(bench.Find(PALM_PRIMARY_NAME), "ana el (kumanda çerçevesi)");
            DrawPalmFrame(bench.Find(PALM_SECONDARY_NAME), "ön kabza eli");
        }

        private void DrawPalmFrame(Transform palm, string label)
        {
            if (palm == null)
            {
                return;
            }

            const float Length = 0.06f;
            Vector3 origin = palm.position;

            Handles.color = Color.blue;
            Handles.DrawLine(origin, origin + palm.forward * Length);
            Handles.color = Color.green;
            Handles.DrawLine(origin, origin + palm.up * Length);
            Handles.color = Color.red;
            Handles.DrawLine(origin, origin + palm.right * Length);

            Handles.color = Color.white;
            Handles.Label(origin + palm.up * (Length + 0.01f), label, LabelStyle());
        }

        /// <summary>
        /// Ölçüleri çizer.
        /// <para>⚠️ <b>Ölçülerin hedefi kavrama noktası DEĞİL silahın gerçek geometrisidir.</b> El
        /// modelinin bileği zaten kavrama noktasının üstünde duruyor; oraya olan her mesafe tanımı
        /// gereği neredeyse sabittir (ölçülen şey elin kendi boyu olur, kavramanın kalitesi değil).
        /// Bu yüzden hedef, ada göre bulunan kabza/ön kabza/tetik düğümüdür.</para>
        /// <para>⚠️ Yüzey mesafesi <see cref="Renderer.bounds"/> (dünya AABB) üzerinden alınır, yani
        /// mesh'e tam oturmayan bir YAKLAŞIMDIR. Kabza ve tetik gibi küçük parçalarda kutu yeterince
        /// dardır; sayı zaten mutlak bir değer için değil, el sürüklenirken <b>değişimi izlemek</b>
        /// için var.</para>
        /// </summary>
        private void DrawMeasurements(Transform bench)
        {
            GUIStyle style = LabelStyle();
            Transform weapon = bench.Find(WEAPON_NODE_NAME);
            if (weapon == null)
            {
                return;
            }

            for (int i = 0; i < _measures.Count; i++)
            {
                HandMeasure measure = _measures[i];

                if (measure.Node == null)
                {
                    // El silinmiş: liste bayat, bir sonraki karede yeniden kurulur.
                    _measuresDirty = true;
                    continue;
                }

                Handles.color = Color.white;

                Renderer gripPart = FindWeaponPart(weapon,
                    measure.Kind == GripSocketKind.Primary ? GRIP_KEYS : FOREGRIP_KEYS);

                if (measure.Palm != null && gripPart != null)
                {
                    Vector3 palm = measure.Palm.position;
                    Vector3 surface = gripPart.bounds.ClosestPoint(palm);
                    Handles.DrawDottedLine(palm, surface, 3f);
                    Handles.Label(
                        Vector3.Lerp(palm, surface, 0.5f) + Vector3.up * 0.01f,
                        $"avuç → {GripPartLabel(measure.Kind)} yüzeyi: {Centimeters(palm, surface)}",
                        style);
                }

                // ⚠️ Tetik ölçüsü YALNIZ ana el için çizilir: ön kabzayı tutan el tetiği çekmez,
                // orada bu sayı anlamsız (ve yanıltıcı) olurdu.
                if (measure.Kind != GripSocketKind.Primary || measure.IndexTip == null)
                {
                    continue;
                }

                Renderer trigger = FindWeaponPart(weapon, TRIGGER_KEYS);
                if (trigger != null)
                {
                    Vector3 index = measure.IndexTip.position;
                    Vector3 triggerSurface = trigger.bounds.ClosestPoint(index);
                    Handles.DrawDottedLine(index, triggerSurface, 3f);
                    Handles.Label(
                        Vector3.Lerp(index, triggerSurface, 0.5f),
                        $"işaret parmağı ucu → tetik: {Centimeters(index, triggerSurface)}",
                        style);
                }
            }
        }

        /// <summary>
        /// Modelde bulunamayan ölçü hedeflerini bildirir.
        /// <para>⚠️ Bu bilgi <see cref="Debug.Log"/> ile verilmez: ölçüler <c>duringSceneGui</c>'de,
        /// bu pencere de <c>OnGUI</c>'de her karede koşuyor — tek bir eksik düğüm konsolu saniyede
        /// onlarca satırla boğardı. Eksik hedef yine de SESSİZ bırakılmaz, yoksa kullanıcı çizilmeyen
        /// ölçüyü aracın bozukluğu sanır.</para>
        /// </summary>
        private void DrawMissingPartsSection(WeaponDefinition definition)
        {
            Transform bench = FindBenchRoot();
            Transform weapon = bench == null ? null : bench.Find(WEAPON_NODE_NAME);
            if (weapon == null)
            {
                return;
            }

            var missing = new List<string>();

            if (FindWeaponPart(weapon, GRIP_KEYS) == null)
            {
                missing.Add("Bu modelde kabza düğümü yok (grip/handle) — avuç–kabza ölçüsü gizlendi.");
            }

            if (FindWeaponPart(weapon, TRIGGER_KEYS) == null)
            {
                missing.Add("Bu modelde tetik düğümü yok (trigger) — parmak–tetik ölçüsü gizlendi.");
            }

            if (definition != null && definition.IsTwoHanded && FindWeaponPart(weapon, FOREGRIP_KEYS) == null)
            {
                missing.Add("Bu modelde ön kabza düğümü yok (handguard/barrelguard) — " +
                            "avuç–ön kabza ölçüsü gizlendi.");
            }

            if (missing.Count == 0)
            {
                return;
            }

            EditorGUILayout.HelpBox(string.Join("\n", missing), MessageType.Info);
            EditorGUILayout.Space();
        }

        private static string GripPartLabel(GripSocketKind kind)
        {
            return kind == GripSocketKind.Primary ? "kabza" : "ön kabza";
        }

        /// <summary>
        /// Tezgâhtaki silahın alt ağacında, adında anahtarlardan biri geçen ilk <see cref="Renderer"/>.
        /// Anahtarlar <b>sırayla</b> denenir, ilk tutan kazanır (spesifik olan listede önce durur).
        /// <para>⚠️ Sonuç — bulunamama dahil — önbelleğe alınır: bu yol <c>duringSceneGui</c>'den her
        /// karede çağrılıyor.</para>
        /// </summary>
        private Renderer FindWeaponPart(Transform weapon, string[] keywords)
        {
            if (weapon == null)
            {
                return null;
            }

            string cacheKey = keywords[0];
            if (_partCache.TryGetValue(cacheKey, out Renderer cached))
            {
                return cached;
            }

            Renderer found = null;
            for (int i = 0; i < keywords.Length && found == null; i++)
            {
                found = SearchPartRenderer(weapon, keywords[i]);
            }

            _partCache[cacheKey] = found;
            return found;
        }

        /// <summary>
        /// Ada göre derinlik-öncelikli arama; <see cref="IsMeasurementNoise"/> dediği dallara girmez.
        /// </summary>
        private static Renderer SearchPartRenderer(Transform node, string keyword)
        {
            if (IsMeasurementNoise(node))
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
        /// Ölçüye girmemesi gereken dallar: kavrama pozu düğümleri, <b>el modelleri</b>, kavrama
        /// çerçevesi.
        /// <para>⚠️ Bu eleme olmadan araç <b>kendi çizdiği şeyi ölçerdi</b> — el modelinin ve çerçeve
        /// prefabının altında da Renderer var ve adları aranan anahtarlarla çakışabiliyor.</para>
        /// <para>⚠️ <see cref="HideFlags.DontSave"/> burada eleme ÖLÇÜTÜ DEĞİLDİR: tezgâhın tamamı o
        /// bayrakla kuruluyor, ölçüt olsaydı silahın hiçbir parçası bulunamazdı.</para>
        /// </summary>
        private static bool IsMeasurementNoise(Transform node)
        {
            if (node.name == ItemGripPoses.RootNodeName || node.name == ItemHandRig.RootNodeName ||
                node.name == HAND_NODE_NAME)
            {
                return true;
            }

            return node.GetComponent<WeaponFrame>() != null;
        }

        private static string Centimeters(Vector3 a, Vector3 b)
        {
            return $"{Vector3.Distance(a, b) * 100f:0.0} cm";
        }

        private GUIStyle LabelStyle()
        {
            if (_labelStyle == null)
            {
                // Handles.color etiketi boyamaz; rengi stil taşır.
                _labelStyle = new GUIStyle(EditorStyles.label);
                _labelStyle.normal.textColor = Color.white;
            }

            return _labelStyle;
        }

        // -------------------------------------------------------------------- varsayılan poz

        private HandPose DefaultHandPose(Handedness handedness)
        {
            if (handedness == Handedness.Left)
            {
                return _defaultLeftPose ?? (_defaultLeftPose = CreateDefaultHandPose(Handedness.Left));
            }

            return _defaultRightPose ?? (_defaultRightPose = CreateDefaultHandPose(Handedness.Right));
        }

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
        private static HandPose CreateDefaultHandPose(Handedness handedness)
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
