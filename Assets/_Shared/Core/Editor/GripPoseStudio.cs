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

namespace VortexArena.Core.Editor
{
    /// <summary>
    /// <c>Tools &gt; VortexArena &gt; Weapons &gt; Kavrama Pozu Stüdyosu</c> — silahın elde nasıl
    /// duracağını <b>gözlük takmadan</b> Scene view'da görme aracı: seçili silahın kavrama
    /// soketlerine ISDK'nın hayalet elini oturtur ve işaretçiyi sürüklerken aynı karede takip
    /// ettirir.
    /// <para>
    /// <b>Aracın var olma sebebi geri besleme süresidir:</b> kavrama ofseti tek doğru değeri olan
    /// bir sayı değil, "avuç kabzaya değiyor mu / işaret parmağı tetiğe ulaşıyor mu" sorusunun
    /// cevabıdır. O soruyu APK build'i + gözlük turuyla sormak her denemeyi dakikalara çıkarıyordu.
    /// </para>
    /// <para>
    /// ⚠️ <b>Bu pencere hiçbir şey YAZMAZ — ne <c>WD_*.asset</c>'e ne de silahın duruşuna.</b>
    /// Kavrama duruşunun tek yazma yolu <c>Tools &gt; VortexArena &gt; Weapons &gt; Write Grip
    /// Sockets To Definition</c>'dır. İkinci bir yazma yolu aynı ters/düz bileşim matematiğini
    /// ikinci kez uygulardı ve biri düzeltilip öteki unutulduğunda silah sessizce yanlış dururdu
    /// (o asimetrinin anlatımı <see cref="ItemDefinition"/> başındadır).
    /// </para>
    /// <para>
    /// ⚠️ <b>Hayalet eller sahneye AİT DEĞİLDİR</b> — <see cref="HideFlags.HideAndDontSave"/> ile
    /// örneklenir, pencere kapanınca yok edilir ve hiyerarşide görünmez. Kavrama pozu bir <i>yazma
    /// aracıdır</i>, oyun onu okumaz: hayaletin sahneye kaydedilmesi her silahın yanında oyunda da
    /// çizilen bir el bırakırdı.
    /// </para>
    /// <para>
    /// ⚠️ <b>Dialog YOK</b> (<c>WeaponKitBuilder</c> / <c>GripSocketAuthoring</c> ile aynı gerekçe):
    /// modal dialog Unity ana thread'ini kilitler ve CLI/pipeline üzerinden çalıştırıldığında komut
    /// timeout verir. Sonuç <see cref="Debug.Log"/> ile bildirilir.
    /// </para>
    /// </summary>
    internal sealed class GripPoseStudio : EditorWindow
    {
        private const string LOG = "[GripPoseStudio]";

        /// <summary>
        /// ISDK'nın hayalet el sağlayıcısı — <b>OpenXR</b> iskeleti için.
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
        /// Hayalet örneklerinin ad öneki. Domain reload hayalet referanslarını düşürür ama
        /// <c>DontSave</c> objeleri hayatta kalabilir; ön ek olmadan o yetimleri bir daha kimse
        /// bulamazdı (görünmezler, sahneye de kaydedilmezler).
        /// </summary>
        private const string GHOST_NAME_PREFIX = "VA_GripGhost_";

        /// <summary>Bilek pozunun nereden okunacağı.</summary>
        private enum PoseSource
        {
            /// <summary>Sahnedeki <see cref="GripSocketMarker"/> — henüz SO'ya yazılmamış değer.</summary>
            Marker = 0,

            /// <summary><see cref="ItemDefinition"/> alanları — oyunun gerçekten kullandığı değer.</summary>
            Definition = 1
        }

        /// <summary>Ekranda duran tek bir hayalet el ve ondan türetilen ölçüm noktaları.</summary>
        private sealed class GhostSlot
        {
            public GripSocketKind Kind;
            public bool RightHand;
            public HandGhost Ghost;

            /// <summary>
            /// İşaret parmağının ucu; hayalet rig'inde yoksa <c>null</c> (ölçü sessizce atlanır).
            /// <para>⚠️ Tetiği çeken parmak <b>işaret</b> parmağıdır — konuşurken "baş parmak
            /// tetiğe değiyor mu" denmesi ölçünün baş parmağa bağlanmasını gerektirmez.</para>
            /// </summary>
            public Transform IndexTip;

            /// <summary>Avucun kabzaya değen noktası (orta parmak boğumu); yoksa bilek kökü.</summary>
            public Transform Palm;
        }

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

        private Weapon _target;
        private PoseSource _source = PoseSource.Marker;
        private bool _primaryRightHand = true;
        private bool _secondaryRightHand;

        private readonly List<GhostSlot> _slots = new List<GhostSlot>();
        private bool _slotsDirty = true;

        /// <summary>
        /// Anahtar kümesi → hedef silahtaki parça (bulunamadıysa <c>null</c> olarak da kaydedilir).
        /// <para>⚠️ Önbellek ŞART: <c>duringSceneGui</c> her karede koşuyor, taramasız hâlde her
        /// kare silahın tüm alt ağacını üç kez gezerdi. Hedef değişiminde temizlenir.</para>
        /// </summary>
        private readonly Dictionary<string, Renderer> _partCache = new Dictionary<string, Renderer>();

        private HandGhostProvider _ghostProvider;

        /// <summary>Yüklü sağlayıcının yolu — dal ölçümü değişince yeniden yükleneceğini bilmek için.</summary>
        private string _ghostProviderPath;

        /// <summary>
        /// Sağlayıcı hiç bulunamadığında uyarı bir kez basılsın diye.
        /// <para>⚠️ Bu yol <c>duringSceneGui</c>'den her karede geçiyor: bayraksız hâlde eksik bir
        /// asset konsolu saniyede onlarca satırla boğardı.</para>
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
            window.titleContent = new GUIContent("Kavrama Pozu");
            window.minSize = new Vector2(320f, 340f);
            window.Show();
        }

        private void OnEnable()
        {
            SceneView.duringSceneGui += OnSceneGui;
            AssemblyReloadEvents.beforeAssemblyReload += DestroyGhosts;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;

            SweepOrphanGhosts();
            ResolveTarget();
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGui;
            AssemblyReloadEvents.beforeAssemblyReload -= DestroyGhosts;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;

            DestroyGhosts();
            SceneView.RepaintAll();
        }

        private void OnPlayModeChanged(PlayModeStateChange change)
        {
            // Play'e girerken/çıkarken sahne yeniden yüklenir; elde kalan hayalet referansı
            // ölü olur ve bir sonraki çizimde null erişim üretirdi.
            DestroyGhosts();
            _slotsDirty = true;
            Repaint();
        }

        private void OnSelectionChange()
        {
            ResolveTarget();
            Repaint();
        }

        /// <summary>
        /// Hedef = seçimin üstündeki ilk <see cref="Weapon"/>. Ebeveyne bakılır çünkü kullanıcı
        /// çoğu zaman kavrama işaretçisini ya da <c>Model</c> alt objesini seçili tutar.
        /// </summary>
        private void ResolveTarget()
        {
            GameObject context = Selection.activeGameObject;
            Weapon found = context == null ? null : context.GetComponentInParent<Weapon>();

            if (found != _target)
            {
                _target = found;
                DestroyGhosts();
            }

            // Parça önbelleği hedefe bağlıdır; seçim her değiştiğinde (aynı silah bile olsa)
            // düşürülür — düğüm adı sahnede değiştirilmiş olabilir ve bayat kayıt sessizce
            // yanlış parçayı ölçerdi.
            _partCache.Clear();
            _slotsDirty = true;

            // Dal ölçümü de düşürülür: sonda olarak hedefin KENDİ poz düğümü kullanılıyor, yani
            // ölçüm hedefe bağlı bir yoldan geliyor. Yeni hedefte baştan ölçmek, bayat bir sondadan
            // gelen sonuca güvenmekten ucuz.
            _openXrHandBranch = null;
        }

        // ------------------------------------------------------------------------------ GUI

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Kavrama Pozu Stüdyosu", EditorStyles.boldLabel);

            if (EditorApplication.isPlaying)
            {
                DestroyGhosts();
                EditorGUILayout.HelpBox(
                    "Play kipinde hayalet çizilmez — çalışan oyunda elin gerçeği zaten sentetik eldir.",
                    MessageType.Info);
                return;
            }

            if (_target == null)
            {
                EditorGUILayout.HelpBox(
                    "Hedef yok: sahnedeki (ya da prefab kipindeki) bir silahı seç. Silahın altındaki " +
                    "bir parçayı seçmek de yeter.",
                    MessageType.Info);
                return;
            }

            EditorGUILayout.ObjectField("Silah", _target, typeof(Weapon), true);

            WeaponDefinition definition = _target.Definition;
            if (definition == null)
            {
                EditorGUILayout.HelpBox(
                    "Silahın tanımı (WeaponDefinition) atanmamış — yalnız işaretçi kaynağı kullanılabilir.",
                    MessageType.Warning);
            }

            EditorGUILayout.Space();

            DrawSourceSection();
            EditorGUILayout.Space();
            DrawHandednessSection(definition);
            EditorGUILayout.Space();
            DrawPoseSection(definition);
            EditorGUILayout.Space();
            DrawMissingPartsSection(definition);
            DrawGhostSourceSection();

            EditorGUILayout.HelpBox(
                "Bu pencere hiçbir şey yazmaz. Ayarı kalıcı yapmak için: " +
                "Tools > VortexArena > Weapons > Write Grip Sockets To Definition.",
                MessageType.None);
        }

        private void DrawSourceSection()
        {
            EditorGUILayout.LabelField("Bilek pozu kaynağı", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            int index = GUILayout.Toolbar((int)_source, new[] { "İşaretçi", "Tanım" });
            if (EditorGUI.EndChangeCheck())
            {
                _source = (PoseSource)index;
                SceneView.RepaintAll();
            }

            // Bu ayrım projede zaten sarı (işaretçi) / camgöbeği (tanım) gizmo farkıyla var;
            // burada tek satırla tekrarlanıyor çünkü iki hayalet çakışmadığında ilk soru bu oluyor.
            EditorGUILayout.LabelField(
                "İşaretçi = yazılmayı bekleyen, Tanım = oyunun kullandığı.",
                EditorStyles.miniLabel);
        }

        private void DrawHandednessSection(WeaponDefinition definition)
        {
            EditorGUILayout.LabelField("Eller", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            _primaryRightHand = EditorGUILayout.Toggle("Ana el sağ", _primaryRightHand);

            bool twoHanded = definition != null && definition.IsTwoHanded;
            using (new EditorGUI.DisabledScope(!twoHanded))
            {
                _secondaryRightHand = EditorGUILayout.Toggle("Ön kabza eli sağ", _secondaryRightHand);
            }

            if (EditorGUI.EndChangeCheck())
            {
                // El değişince hayalet prefabı da değişir (sağ/sol ayrı model) → baştan kurulur.
                DestroyGhosts();
                _slotsDirty = true;
                SceneView.RepaintAll();
            }

            if (!twoHanded)
            {
                EditorGUILayout.LabelField(
                    "Silah tek elli (holdMode = OneHand) — ön kabza hayaleti çizilmez.",
                    EditorStyles.miniLabel);
            }
        }

        private void DrawPoseSection(WeaponDefinition definition)
        {
            EditorGUILayout.LabelField("Parmak pozu", EditorStyles.boldLabel);

            DrawPoseRow(GripSocketKind.Primary, _primaryRightHand);

            if (definition != null && definition.IsTwoHanded)
            {
                DrawPoseRow(GripSocketKind.Secondary, _secondaryRightHand);
            }

            EditorGUILayout.Space();

            if (GUILayout.Button("Tüm Silahlara Düğüm Aç"))
            {
                CreateNodesForAllWeapons();
            }

            EditorGUILayout.LabelField(
                "\"Parmakları Düzenle\" pozu seçer; parmak tutamaklarını ISDK'nın kendi " +
                "HandGrabPose editörü Scene view'da çizer.",
                EditorStyles.miniLabel);
        }

        private void DrawPoseRow(GripSocketKind kind, bool rightHand)
        {
            string title = $"{kind} ({(rightHand ? "sağ" : "sol")})";
            HandGrabPose node = FindPoseNode(_target, kind, rightHand);

            // Eldeki ilk gerçek düğüm aynı zamanda el dalının sondasıdır (bkz. TryMeasureHandBranch).
            TryMeasureHandBranch(node);

            EditorGUILayout.LabelField(title, node == null ? "poz yok" : node.name);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (node == null)
                {
                    if (GUILayout.Button("Poz Düğümü Üret"))
                    {
                        EnsurePoseNode(_target, kind, rightHand, out bool created);
                        if (created)
                        {
                            Debug.Log($"{LOG} '{_target.name}' altında {ItemGripPoses.RootNodeName}/" +
                                      $"{ItemGripPoses.NodeName(kind, rightHand)} üretildi — parmakları " +
                                      "\"Parmakları Düzenle\" ile bük.", _target);
                        }

                        _slotsDirty = true;
                    }

                    return;
                }

                if (GUILayout.Button("Parmakları Düzenle"))
                {
                    Selection.activeGameObject = node.gameObject;
                }

                if (GUILayout.Button("Karşı Ele Aynala"))
                {
                    MirrorToOppositeHand(kind, rightHand);
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
            var missing = new List<string>();

            if (FindWeaponPart(GRIP_KEYS) == null)
            {
                missing.Add("Bu modelde kabza düğümü yok (grip/handle) — avuç–kabza ölçüsü gizlendi.");
            }

            if (FindWeaponPart(TRIGGER_KEYS) == null)
            {
                missing.Add("Bu modelde tetik düğümü yok (trigger) — parmak–tetik ölçüsü gizlendi.");
            }

            if (definition != null && definition.IsTwoHanded && FindWeaponPart(FOREGRIP_KEYS) == null)
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

        /// <summary>
        /// Hangi hayalet sağlayıcısının kullanıldığını (ve dalın ölçülüp ölçülemediğini) tek satırda
        /// yazar.
        /// <para>⚠️ Bu satır süs değil <b>teşhistir</b>: yanlış dalın sağlayıcısı yüklendiğinde el
        /// yine çizilir, yalnız iskeleti tutmaz — belirtisi "hayalet garip duruyor" olur ve sebebi
        /// hiçbir yerde görünmezdi.</para>
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

            EditorGUILayout.HelpBox($"El dalı: {measured} · hayalet: {asset}", MessageType.None);
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
        private void TryMeasureHandBranch(HandGrabPose probe)
        {
            if (_openXrHandBranch.HasValue || probe == null)
            {
                return;
            }

            if (TargetHandPoseField == null)
            {
                // Alan hiç yoksa paket sözleşmesi değişmiş demektir; ISDK'nın gittiği yön OpenXR,
                // yanlışsa sağlayıcı yükleme adımı zaten ötekine düşer.
                _openXrHandBranch = true;
                _slotsDirty = true;
                return;
            }

            HandPose live = probe.HandPose;
            if (live == null)
            {
                return; // sonda geçersiz — ÖLÇÜLMEDİ sayılır, bir sonraki düğümde tekrar denenir
            }

            _openXrHandBranch = ReferenceEquals(TargetHandPoseField.GetValue(probe), live);

            // Dal değişimi hayaletin rig'ini değiştirir: eldeki örnekler baştan kurulur.
            _slotsDirty = true;
        }

        // ---------------------------------------------------------------------- poz düğümleri

        /// <summary>
        /// Adı <see cref="ItemGripPoses"/>'den gelen poz düğümü; <b>poz verisi taşımasa da</b>
        /// döner.
        /// <para>⚠️ <see cref="ItemGripPoses.Find"/> kullanılmaz: o, tüketici tarafı için
        /// <c>UsesHandPose</c> false olan düğümü "yok" sayar. Stüdyonun ise henüz parmakları
        /// bükülmemiş bir düğümü <b>bulup</b> göstermesi gerekir, yoksa "Poz Düğümü Üret" her
        /// tıklamada ikinci bir düğüm üretirdi.</para>
        /// </summary>
        private static HandGrabPose FindPoseNode(Weapon weapon, GripSocketKind kind, bool rightHand)
        {
            if (weapon == null)
            {
                return null;
            }

            Transform root = weapon.transform.Find(ItemGripPoses.RootNodeName);
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
        /// hesaplamaya başlar ve bugünkü kavrama hissi sessizce değişir (aynı gerekçeyle silah
        /// kitine <c>HandGrabPose</c> çocuğu eklenmiyor). Bu düğümler saf VERİDİR; onları yalnız
        /// <c>HandGripPoser</c> okur.
        /// </para>
        /// <para>
        /// ⚠️ Boş bir <see cref="HandPose"/> atanmaz — parametresiz kurucu iskeleti <b>sol</b> el
        /// varsayımıyla doldurur, <c>HandPose(Handedness)</c> kurucusu ise eklem dizisini sıfır
        /// quaternion'larla bırakır. İkisi de hayaleti çizilemez hâle sokar; bu yüzden varsayılan
        /// poz ilgili elin bind iskeletinden kurulur.
        /// </para>
        /// <para>
        /// ⚠️ <b>Mevcut düğüm YERLEŞTİRİLMEMİŞSE sokete taşınır.</b> <c>WeaponKitBuilder</c> düğümleri
        /// silahın orijininde, sıfır transformla açıyor ve o hâldeki bir düğümü <c>HandGripPoser</c>
        /// bilerek yok sayıyor (<see cref="ItemGripPoses.Find"/>). Burada dokunmasaydık kitin açtığı
        /// düğüm orijinde kalır, bu düğme her basışta "zaten var" der ve poz HİÇ devreye girmezdi —
        /// belirtisi "araç çalışıyor ama el sarılmıyor" olurdu. Elle yerleştirilmiş bir düğüme ise
        /// dokunulmaz: taşıma yalnız birim pozdaki düğüm için yapılır.
        /// </para>
        /// <para>
        /// ⚠️ <b>Kullanılamaz hâldeki poz de onarılır</b> (<see cref="NeedsPoseRepair"/>): düğüm var
        /// ama canlı pozu boş olabilir (poz ISDK'nın dala göre değişen <b>öteki</b> alanına yazılmışsa
        /// getter onu hiç görmez). Onarım kapısı olmasaydı düğüm "zaten var" sayıldığı için hiç
        /// düzelmez, kullanıcının tek çaresi onu elle silmek olurdu. Kullanılabilir bir poz varsa
        /// DOKUNULMAZ — elle bükülmüş parmaklar silinmesin.
        /// </para>
        /// </summary>
        private HandGrabPose EnsurePoseNode(Weapon weapon, GripSocketKind kind, bool rightHand, out bool created)
        {
            created = false;

            HandGrabPose existing = FindPoseNode(weapon, kind, rightHand);
            if (existing != null)
            {
                TryMeasureHandBranch(existing);

                if (NeedsPoseRepair(existing))
                {
                    Undo.RecordObject(existing, "VortexArena Kavrama Pozu");
                    existing.InjectOptionalHandPose(
                        CreateDefaultHandPose(rightHand ? Handedness.Right : Handedness.Left));
                    MarkDirty(weapon);

                    Debug.Log($"{LOG} '{weapon.name}' altındaki " +
                              $"{ItemGripPoses.NodeName(kind, rightHand)} düğümünde kullanılabilir poz " +
                              "yok — varsayılan poz yazıldı, parmakları tekrar bük.", existing);
                }

                // ⚠️ "Yerleştirilmemiş" ölçüsü BURADA TEKRARLANMAZ: kuralın tek tanımı
                // ItemGripPoses'tadır ve tüketici (HandGripPoser) de onu kullanır. İkinci bir eşik
                // yazılsaydı, stüdyonun "yerleşik" saydığı bir düğümü runtime yok sayabilirdi.
                if (ItemGripPoses.IsUnplaced(existing) &&
                    TryGetSocketWorldPose(weapon, kind, out Pose existingSocket))
                {
                    Undo.RecordObject(existing.transform, "VortexArena Kavrama Pozu");
                    existing.transform.SetPositionAndRotation(existingSocket.position, existingSocket.rotation);
                    MarkDirty(weapon);

                    Debug.Log($"{LOG} '{weapon.name}' altındaki " +
                              $"{ItemGripPoses.NodeName(kind, rightHand)} düğümü silahın orijininde " +
                              "duruyordu (silah kiti öyle açar) — sokete taşındı.", existing);
                }

                return existing;
            }

            Transform poseRoot = EnsurePoseRoot(weapon);

            HandGrabPose node = HandGrabUtils.CreateHandGrabPose(poseRoot, weapon.transform);
            node.gameObject.name = ItemGripPoses.NodeName(kind, rightHand);
            node.InjectOptionalHandPose(CreateDefaultHandPose(rightHand ? Handedness.Right : Handedness.Left));

            // Düğüm sokete oturtulur: ISDK'nın kendi HandGrabPose editörü de kendi hayaletini
            // düğümün transformuna çizer, ikisi ayrı yerde durursa hangisinin doğru olduğu
            // belirsizleşir.
            if (TryGetSocketWorldPose(weapon, kind, out Pose socket))
            {
                node.transform.SetPositionAndRotation(socket.position, socket.rotation);
            }

            Undo.RegisterCreatedObjectUndo(node.gameObject, "VortexArena Kavrama Pozu");
            MarkDirty(weapon);

            created = true;
            return node;
        }

        private static Transform EnsurePoseRoot(Weapon weapon)
        {
            Transform root = weapon.transform.Find(ItemGripPoses.RootNodeName);
            if (root != null)
            {
                return root;
            }

            var go = new GameObject(ItemGripPoses.RootNodeName);
            go.transform.SetParent(weapon.transform, false);
            Undo.RegisterCreatedObjectUndo(go, "VortexArena Kavrama Pozu");
            return go.transform;
        }

        /// <summary>
        /// Aynı soketin karşı el pozunu üretir/günceller. Sağ elle yazılan tüfek pozu, silah sol
        /// ele geçtiğinde de bir karşılığı olsun diye.
        /// </summary>
        private void MirrorToOppositeHand(GripSocketKind kind, bool rightHand)
        {
            HandGrabPose source = FindPoseNode(_target, kind, rightHand);
            if (source == null || source.HandPose == null)
            {
                Debug.LogWarning($"{LOG} {kind} ({(rightHand ? "sağ" : "sol")}) pozunda el verisi yok — " +
                                 "aynalanacak bir şey bulunamadı.", _target);
                return;
            }

            HandGrabPose mirror = EnsurePoseNode(_target, kind, !rightHand, out bool _);

            Undo.RecordObject(mirror.transform, "VortexArena Kavrama Pozu Aynala");
            Undo.RecordObject(mirror, "VortexArena Kavrama Pozu Aynala");
            HandGrabUtils.MirrorHandGrabPose(source, mirror, _target.transform);
            MarkDirty(_target);

            _slotsDirty = true;
            SceneView.RepaintAll();

            Debug.Log($"{LOG} {source.name} → {mirror.name} aynalandı. Ayna en iyi tahmindir: " +
                      "karşı elin bileği sahnede elle düzeltilebilir.", mirror);
        }

        /// <summary>
        /// Açık sahnedeki (ya da prefab kipindeki) her silaha eksik poz düğümlerini açar.
        /// İdempotenttir; var olan düğüme dokunmaz.
        /// </summary>
        private void CreateNodesForAllWeapons()
        {
            Weapon[] weapons = CollectWeapons();
            if (weapons.Length == 0)
            {
                Debug.LogWarning($"{LOG} Ortada hiç Weapon yok — sahneyi (ya da bir WPN_* prefabını) aç.");
                return;
            }

            int createdCount = 0;
            for (int i = 0; i < weapons.Length; i++)
            {
                Weapon weapon = weapons[i];

                EnsurePoseNode(weapon, GripSocketKind.Primary, _primaryRightHand, out bool primaryCreated);
                if (primaryCreated)
                {
                    createdCount++;
                }

                WeaponDefinition definition = weapon.Definition;
                if (definition == null || !definition.IsTwoHanded)
                {
                    continue;
                }

                EnsurePoseNode(weapon, GripSocketKind.Secondary, _secondaryRightHand, out bool secondaryCreated);
                if (secondaryCreated)
                {
                    createdCount++;
                }
            }

            _slotsDirty = true;
            Debug.Log($"{LOG} {weapons.Length} silah gezildi, {createdCount} yeni poz düğümü açıldı " +
                      "(var olanlara dokunulmadı).");
        }

        private static Weapon[] CollectWeapons()
        {
            PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage != null)
            {
                return stage.prefabContentsRoot.GetComponentsInChildren<Weapon>(true);
            }

            return FindObjectsByType<Weapon>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        }

        /// <summary>
        /// ⚠️ Prefab kipinde <see cref="EditorSceneManager.MarkSceneDirty(Scene)"/> işe yaramaz —
        /// prefab stage'in önizleme sahnesi kaydedilmez, kaydedilen ASSET'tir. Bu yüzden stage
        /// varken kirletilen kök objedir.
        /// </summary>
        private static void MarkDirty(Weapon weapon)
        {
            PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage != null && stage.IsPartOfPrefabContents(weapon.gameObject))
            {
                EditorUtility.SetDirty(stage.prefabContentsRoot);
                return;
            }

            EditorSceneManager.MarkSceneDirty(weapon.gameObject.scene);
        }

        // -------------------------------------------------------------------------- hayaletler

        private void OnSceneGui(SceneView sceneView)
        {
            if (EditorApplication.isPlaying)
            {
                DestroyGhosts();
                return;
            }

            if (_target == null)
            {
                DestroyGhosts();
                return;
            }

            if (_slotsDirty)
            {
                RebuildSlots();
            }

            // ⚠️ Duruş HER çizimde tazelenir: kullanıcının asıl beklentisi işaretçiyi sürüklerken
            // elin AYNI karede takip etmesi. Olay tabanlı bir tazeleme (yalnız değişimde) sürükleme
            // sırasında bir kare geriden gelirdi.
            RefreshGhostPoses();
            DrawMeasurements();
        }

        private void RebuildSlots()
        {
            _slotsDirty = false;
            DestroyGhosts();

            if (_target == null || !TryGetGhostProvider(out HandGhostProvider provider))
            {
                return;
            }

            CreateSlot(provider, GripSocketKind.Primary, _primaryRightHand);

            WeaponDefinition definition = _target.Definition;
            if (definition != null && definition.IsTwoHanded)
            {
                CreateSlot(provider, GripSocketKind.Secondary, _secondaryRightHand);
            }
        }

        private void CreateSlot(HandGhostProvider provider, GripSocketKind kind, bool rightHand)
        {
            if (!TryGetSocketWorldPose(_target, kind, out Pose _))
            {
                // Soket çözülemiyorsa hayalet de kurulmaz — el sahnenin orijininde asılı kalırdı.
                return;
            }

            Handedness handedness = rightHand ? Handedness.Right : Handedness.Left;
            HandGhost prototype = provider.GetHand(handedness);
            if (prototype == null)
            {
                return;
            }

            HandGhost ghost = Instantiate(prototype);
            ghost.gameObject.name = $"{GHOST_NAME_PREFIX}{kind}";
            ghost.gameObject.hideFlags = HideFlags.HideAndDontSave;

            // Prefab kipinde önizleme sahnesi ayrıdır: hayalet aktif sahnede kalırsa prefab
            // penceresinde HİÇ çizilmez (ve kullanıcı aracı bozuk sanır).
            SceneManager.MoveGameObjectToScene(ghost.gameObject, _target.gameObject.scene);

            var slot = new GhostSlot
            {
                Kind = kind,
                RightHand = rightHand,
                Ghost = ghost
            };

            ResolveMeasurePoints(slot);
            _slots.Add(slot);
        }

        private void RefreshGhostPoses()
        {
            for (int i = _slots.Count - 1; i >= 0; i--)
            {
                GhostSlot slot = _slots[i];
                if (slot.Ghost == null)
                {
                    _slots.RemoveAt(i);
                    _slotsDirty = true;
                    continue;
                }

                if (!TryGetSocketWorldPose(_target, slot.Kind, out Pose socket))
                {
                    continue;
                }

                HandGrabPose posed = ItemGripPoses.Find(_target.transform, slot.Kind, slot.RightHand);
                TryMeasureHandBranch(posed);

                HandPose handPose = posed != null
                    ? posed.HandPose
                    : DefaultHandPose(slot.RightHand ? Handedness.Right : Handedness.Left);

                slot.Ghost.SetPose(handPose, socket);
            }
        }

        private void DestroyGhosts()
        {
            for (int i = 0; i < _slots.Count; i++)
            {
                if (_slots[i].Ghost != null)
                {
                    DestroyImmediate(_slots[i].Ghost.gameObject);
                }
            }

            _slots.Clear();
        }

        /// <summary>
        /// Domain reload'dan sağ çıkmış yetim hayaletleri toplar. <c>DontSave</c> objeleri sahneye
        /// yazılmaz ama bellekte kalabilir; görünmez oldukları için elle silinemezler.
        /// </summary>
        private static void SweepOrphanGhosts()
        {
            HandGhost[] ghosts = FindObjectsByType<HandGhost>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < ghosts.Length; i++)
            {
                if (ghosts[i] != null && ghosts[i].gameObject.name.StartsWith(GHOST_NAME_PREFIX))
                {
                    DestroyImmediate(ghosts[i].gameObject);
                }
            }
        }

        /// <summary>
        /// Ölçülen el dalına ait hayalet sağlayıcısını yükler; o yükleneMEZse ötekine düşer.
        /// <para>⚠️ Sağlayıcı <b>dala aittir</b>: OpenXR iskeletiyle OVR hayaletini çizmek eli
        /// bozuk bir duruşa sokar (eklem sayısı ve sırası aynı değil). Ölçüm yapılamadıysa OpenXR
        /// varsayılır — paketin <c>versionDefines</c>'ı bu dalı her Unity sürümünde açıyor.</para>
        /// </summary>
        private bool TryGetGhostProvider(out HandGhostProvider provider)
        {
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
                Debug.LogWarning($"{LOG} ISDK hayalet sağlayıcısı iki dalda da bulunamadı " +
                                 $"({GHOST_PROVIDER_PATH_OPENXR} / {GHOST_PROVIDER_PATH_OVR}) — " +
                                 "paket sürümü değiştiyse yolları bu dosyadaki sabitlerden güncelle.");
            }

            return false;
        }

        // ------------------------------------------------------------------------ soket pozu

        /// <summary>
        /// Seçili kaynağa göre soketin DÜNYA pozu.
        /// <para>⚠️ <c>TransformPoint</c> KULLANILMAZ: kavrama ofseti metre cinsindendir ve araya
        /// giren transformların ölçeği ona bulaşmamalı (aynı gerekçe
        /// <c>GripSocketAuthoring.LocalPose</c> ve <c>Weapon.ApplyCanonicalGrip</c> içinde de elle
        /// bileşim yaptırıyor). Bu yüzden yalnız konum farkı + dönüş bileşimi.</para>
        /// <para>⚠️ Tanım kaynağında Primary <b>ters</b> okunur: SO'daki <c>primaryGrip</c>
        /// "el → eşya" yönündedir, işaretçi ise "eşya → el". Secondary zaten eşya-yereldir ve düz
        /// okunur (asimetrinin anlatımı <see cref="ItemDefinition"/> başındadır).</para>
        /// </summary>
        private bool TryGetSocketWorldPose(Weapon weapon, GripSocketKind kind, out Pose pose)
        {
            pose = default;
            if (weapon == null)
            {
                return false;
            }

            Vector3 localPosition;
            Quaternion localRotation;

            if (_source == PoseSource.Marker)
            {
                GripSocketMarker marker = FindMarker(weapon, kind);
                if (marker == null)
                {
                    return false;
                }

                pose = new Pose(marker.transform.position, marker.transform.rotation);
                return true;
            }

            ItemDefinition definition = weapon.Definition;
            if (definition == null)
            {
                return false;
            }

            if (kind == GripSocketKind.Primary)
            {
                localPosition = definition.PrimaryGripPointOnItem;
                localRotation = Quaternion.Inverse(definition.PrimaryGripRotation);
            }
            else
            {
                localPosition = definition.SecondaryGripPosition;
                localRotation = definition.SecondaryGripRotation;
            }

            Transform root = weapon.transform;
            pose = new Pose(
                root.position + root.rotation * localPosition,
                root.rotation * localRotation);
            return true;
        }

        private static GripSocketMarker FindMarker(Weapon weapon, GripSocketKind kind)
        {
            GripSocketMarker[] markers = weapon.GetComponentsInChildren<GripSocketMarker>(true);
            for (int i = 0; i < markers.Length; i++)
            {
                if (markers[i].Kind == kind)
                {
                    return markers[i];
                }
            }

            return null;
        }

        // --------------------------------------------------------------------------- ölçüler

        /// <summary>
        /// Hayaletin ölçüm noktalarını rig'inden çözer.
        /// <para>⚠️ Parmak <b>uçları</b> puppet'ın eklem tablosunda YOKTUR
        /// (<c>FingersMetadata.HAND_JOINT_IDS</c> uçları taşımaz) — bu yüzden son boğumun
        /// (<c>HandIndex3</c>) çocuğuna, o da yoksa ada göre aramaya düşülür. Hiçbiri tutmazsa
        /// ölçü sessizce çizilmez: eksik bir gösterge için hata basmak, aracın asıl işini
        /// (hayaleti göstermek) gürültüye boğardı.</para>
        /// </summary>
        private static void ResolveMeasurePoints(GhostSlot slot)
        {
            var puppet = slot.Ghost.GetComponentInChildren<HandPuppet>(true);
            slot.Palm = slot.Ghost.Root != null ? slot.Ghost.Root : slot.Ghost.transform;

            if (puppet == null)
            {
                return;
            }

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
                    slot.IndexTip = map.transform;
                }
                else if (map.id == HandJointId.HandIndex3)
                {
                    indexLast = map.transform;
                }
                else if (map.id == HandJointId.HandMiddle1)
                {
                    // ⚠️ "Avuç" olarak bilek kökü DEĞİL orta parmağın boğumu alınır: bilek zaten
                    // soketin tam üstüne oturtuluyor, yani ondan ölçülen mesafe tanımı gereği
                    // sıfırdır ve hiçbir soruyu cevaplamaz. Kabzaya değen yer avucun bu noktasıdır.
                    slot.Palm = map.transform;
                }
            }

            if (slot.IndexTip == null && indexLast != null)
            {
                slot.IndexTip = indexLast.childCount > 0 ? indexLast.GetChild(0) : indexLast;
            }

            if (slot.IndexTip == null)
            {
                slot.IndexTip = FindByName(slot.Ghost.transform, "index", "null", "tip");
            }
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
        /// Ölçüleri çizer.
        /// <para>⚠️ <b>Ölçülerin hedefi SOKET DEĞİL silahın gerçek geometrisidir.</b> Hayaletin
        /// bileği <c>SetPose</c> ile soketin tam üstüne oturuyor; sokete olan her mesafe bu yüzden
        /// tanımı gereği neredeyse sabittir (ölçülen şey elin kendi boyu olur, kavramanın kalitesi
        /// değil) ve işaretçi sürüklenirken sayı kıpırdamazdı. Bu yüzden hedef, ada göre bulunan
        /// kabza/ön kabza/tetik düğümüdür.</para>
        /// <para>⚠️ Yüzey mesafesi <see cref="Renderer.bounds"/> (dünya AABB) üzerinden alınır, yani
        /// mesh'e tam oturmayan bir YAKLAŞIMDIR. Kabza ve tetik gibi küçük parçalarda kutu yeterince
        /// dardır; sayı zaten mutlak bir değer için değil, işaretçi sürüklenirken <b>değişimi
        /// izlemek</b> için var.</para>
        /// <para>⚠️ Hedef parça bulunamazsa ölçü <b>hiç çizilmez</b> — sokete düşülmez, çünkü
        /// düzeltilmek istenen yanlış ölçünün ta kendisi odur. Eksiklik pencerede yazılı
        /// (<see cref="DrawMissingPartsSection"/>).</para>
        /// </summary>
        private void DrawMeasurements()
        {
            if (_slots.Count == 0)
            {
                return;
            }

            GUIStyle style = LabelStyle();

            for (int i = 0; i < _slots.Count; i++)
            {
                GhostSlot slot = _slots[i];

                // Soket çözülemiyorsa hayaletin duruşu bir önceki kareden kalmadır; onun üstünden
                // ölçü çizmek bayat bir sayı gösterirdi.
                if (slot.Ghost == null || !TryGetSocketWorldPose(_target, slot.Kind, out Pose _))
                {
                    continue;
                }

                Handles.color = Color.white;

                Renderer gripPart = FindWeaponPart(
                    slot.Kind == GripSocketKind.Primary ? GRIP_KEYS : FOREGRIP_KEYS);

                if (slot.Palm != null && gripPart != null)
                {
                    Vector3 palm = slot.Palm.position;
                    Vector3 surface = gripPart.bounds.ClosestPoint(palm);
                    Handles.DrawDottedLine(palm, surface, 3f);
                    Handles.Label(
                        Vector3.Lerp(palm, surface, 0.5f) + Vector3.up * 0.01f,
                        $"avuç → {GripPartLabel(slot.Kind)} yüzeyi: {Centimeters(palm, surface)}",
                        style);
                }

                // ⚠️ Tetik ölçüsü YALNIZ ana soket için çizilir: ön kabzayı tutan el tetiği çekmez,
                // orada bu sayı anlamsız (ve yanıltıcı) olurdu.
                if (slot.Kind != GripSocketKind.Primary || slot.IndexTip == null)
                {
                    continue;
                }

                Renderer trigger = FindWeaponPart(TRIGGER_KEYS);
                if (trigger != null)
                {
                    Vector3 index = slot.IndexTip.position;
                    Vector3 triggerSurface = trigger.bounds.ClosestPoint(index);
                    Handles.DrawDottedLine(index, triggerSurface, 3f);
                    Handles.Label(
                        Vector3.Lerp(index, triggerSurface, 0.5f),
                        $"işaret parmağı ucu → tetik: {Centimeters(index, triggerSurface)}",
                        style);
                }
            }

            // İki soket arası eksen: iki elli çözümün silahı hizaladığı eksen budur
            // (ItemGripSolver ana sokete oturur, ön kabzaya bakar).
            if (TryGetSocketWorldPose(_target, GripSocketKind.Primary, out Pose primary) &&
                TryGetSocketWorldPose(_target, GripSocketKind.Secondary, out Pose secondary))
            {
                Handles.color = Color.cyan;
                Handles.DrawLine(primary.position, secondary.position);
                Handles.Label(
                    Vector3.Lerp(primary.position, secondary.position, 0.5f),
                    $"soket ekseni: {Centimeters(primary.position, secondary.position)}",
                    style);
            }
        }

        private static string GripPartLabel(GripSocketKind kind)
        {
            return kind == GripSocketKind.Primary ? "kabza" : "ön kabza";
        }

        /// <summary>
        /// Hedef silahın alt ağacında, adında anahtarlardan biri geçen ilk <see cref="Renderer"/>.
        /// Anahtarlar <b>sırayla</b> denenir, ilk tutan kazanır (spesifik olan listede önce durur).
        /// <para>⚠️ Sonuç — bulunamama dahil — önbelleğe alınır: bu yol <c>duringSceneGui</c>'den
        /// her karede çağrılıyor.</para>
        /// </summary>
        private Renderer FindWeaponPart(string[] keywords)
        {
            if (_target == null)
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
                found = SearchPartRenderer(_target.transform, keywords[i]);
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
        /// Ölçüye girmemesi gereken dallar: kavrama pozu düğümleri, kavrama çerçevesi, soket
        /// işaretçileri ve aracın kendi geçici objeleri.
        /// <para>⚠️ Bu eleme olmadan araç <b>kendi çizdiği şeyi ölçerdi</b> — çerçeve prefabının
        /// ya da işaretçinin altında da Renderer var ve adları ("Grip…") aranan anahtarlarla
        /// çakışıyor.</para>
        /// </summary>
        private static bool IsMeasurementNoise(Transform node)
        {
            if (node.name == ItemGripPoses.RootNodeName)
            {
                return true;
            }

            if (node.GetComponent<WeaponFrame>() != null || node.GetComponent<GripSocketMarker>() != null)
            {
                return true;
            }

            // Hayaletler silahın altında değil ama aynı elemede tutuluyor: gelecekte biri onları
            // silahın altına asarsa ölçü sessizce kendine bakmaya başlardı.
            return (node.gameObject.hideFlags & HideFlags.DontSave) != 0;
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
        /// Bir poz düğümünün <b>canlı</b> pozu kullanılamaz hâlde mi — yani varsayılanın yeniden
        /// yazılması gerekiyor mu.
        /// <para>
        /// ⚠️ Ölçü <see cref="HandGrabPose.HandPose"/> <b>public getter'ı</b> üzerinden alınır, alan
        /// adıyla değil: o getter ISDK'nın kendi derlemesinde doğru dala çözülür ve gerçekte hangi
        /// pozun okunduğunu yalnız o bilir. Alan adına bakan bir kontrol, adı <c>#if</c> ile ikiye
        /// ayrılmış bir alanda sessizce yanlış yeri okurdu.
        /// </para>
        /// <para>
        /// "Tamamı sıfır quaternion" da bozuk sayılır: <c>new HandPose(handedness)</c> eklem dizisini
        /// doldurmadan bırakır ve o dizi puppet'ı çizilemez bir duruşa sokar (ekranda hiç el yoktur,
        /// hata da yoktur).
        /// </para>
        /// <para>⚠️ Tanım <b>tek yerdedir</b>: <c>WeaponKitBuilder</c>'ın onarım kapısı da bunu
        /// çağırır. İki ayrı ölçüt yazılsaydı biri "bozuk" derken öteki "sağlam" diyebilirdi.</para>
        /// </summary>
        internal static bool NeedsPoseRepair(HandGrabPose pose)
        {
            if (pose == null)
            {
                return false;
            }

            HandPose live = pose.HandPose;
            if (live == null)
            {
                return true;
            }

            Quaternion[] rotations = live.JointRotations;
            if (rotations == null || rotations.Length == 0)
            {
                return true;
            }

            for (int i = 0; i < rotations.Length; i++)
            {
                Quaternion rotation = rotations[i];
                if (rotation.x != 0f || rotation.y != 0f || rotation.z != 0f || rotation.w != 0f)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// İlgili elin <b>bind</b> iskeletinden kurulmuş poz (ISDK'nın varsayılan duruşu).
        /// <para>⚠️ <c>new HandPose(handedness)</c> tek başına yetmez: o kurucu eklem dizisini
        /// doldurmaz, dizideki quaternion'lar sıfır kalır ve puppet eli çizilemez bir duruşa
        /// sokar.</para>
        /// <para>
        /// <see cref="HandSkeleton"/> ve <see cref="FingersMetadata"/> burada <b>dala bağlı
        /// değildir</b>: ISDK iki iskelet tablosunu da yazmış ama namespace'i dala göre
        /// anahtarlıyor — <c>Oculus.Interaction.Input.HandSkeleton</c> adı hangi dal derlendiyse
        /// onun tablosuna çözülür. Yani burada ek bir seçim adımı YOK; hayalet sağlayıcısının
        /// aksine (o bir asset yoludur, ad çözümlemesi onu bulamaz).
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
