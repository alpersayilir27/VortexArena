using System.Collections.Generic;
using System.Reflection;
using Oculus.Interaction.HandGrab;
using Oculus.Interaction.HandGrab.Visuals;
using Oculus.Interaction.Input;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VortexArena.Core.Combat;

namespace VortexArena.Core.Editor
{
    /// <summary>
    /// <c>Tools &gt; VortexArena &gt; Weapons &gt; Kavrama Pozu Stüdyosu</c> — silahın elde nasıl
    /// duracağını <b>gözlük takmadan</b> ayarlama aracı: silahın üstüne gerçek bir el modeli oturtur,
    /// sen onu kabzaya yerleştirip parmaklarını bükersin, <b>Bake</b> gerekli veriyi çıkarıp eli
    /// gizler.
    /// <para>
    /// <b>Aracın var olma sebebi geri besleme süresidir:</b> kavrama ofseti tek doğru değeri olan bir
    /// sayı değil, "avuç kabzaya değiyor mu / işaret parmağı tetiğe ulaşıyor mu" sorusunun cevabıdır.
    /// O soruyu APK build'i + gözlük turuyla sormak her denemeyi dakikalara çıkarıyordu.
    /// </para>
    /// <para>
    /// ⚠️ <b>ELLE YAZILAN TEK ŞEY EL MODELİDİR</b> (<see cref="ItemHandRig"/>). Kavrama alanları
    /// (<c>WD_*.asset</c>) ve parmak pozu düğümleri (<see cref="ItemGripPoses"/>) bake'in
    /// <b>ÇIKTISIDIR</b>: pencere onları yalnız durum olarak gösterir, elle düzenlemeye yol açmaz.
    /// Aynı kavramayı iki yerde tarif etmek — bir sayı alanında ve bir el modelinde — ikisinin
    /// zamanla birbirinden sapması demekti ve belirtisi "silah bazı yerlerde doğru duruyor" oluyordu.
    /// </para>
    /// <para>
    /// ⚠️ <b>Poz düğümü seçilmez, oraya yönlendiren düğme YOKTUR.</b> Bir <see cref="HandGrabPose"/>
    /// seçildiğinde ISDK'nın kendi editörü düğümün altına geçici bir hayalet el örnekler
    /// (<c>HandGrabPoseEditor</c>, <see cref="HideFlags.HideAndDontSave"/>) — sahnede birden fazla el
    /// belirir ve hangisinin gerçek kaynak olduğu belirsizleşir. Kaynak daima
    /// <c>Hands/Hand_&lt;Kind&gt;</c>'dır.
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

        private Weapon _target;

        private readonly List<HandMeasure> _measures = new List<HandMeasure>();
        private bool _measuresDirty = true;

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
            window.titleContent = new GUIContent("Kavrama Pozu");
            window.minSize = new Vector2(320f, 300f);
            window.Show();
        }

        private void OnEnable()
        {
            SceneView.duringSceneGui += OnSceneGui;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            ResolveTarget();
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGui;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            SceneView.RepaintAll();
        }

        private void OnPlayModeChanged(PlayModeStateChange change)
        {
            // Play'e girerken/çıkarken sahne yeniden yüklenir; eldeki transform referansları ölü
            // olur ve bir sonraki çizimde null erişim üretirdi.
            _measuresDirty = true;
            Repaint();
        }

        private void OnSelectionChange()
        {
            ResolveTarget();
            Repaint();
        }

        /// <summary>
        /// Hedef = seçimin üstündeki ilk <see cref="Weapon"/>. Ebeveyne bakılır çünkü kullanıcı
        /// çoğu zaman el modelini ya da <c>Model</c> alt objesini seçili tutar.
        /// </summary>
        private void ResolveTarget()
        {
            GameObject context = Selection.activeGameObject;
            _target = context == null ? null : context.GetComponentInParent<Weapon>();

            // Parça önbelleği hedefe bağlıdır; seçim her değiştiğinde (aynı silah bile olsa)
            // düşürülür — düğüm adı sahnede değiştirilmiş olabilir ve bayat kayıt sessizce
            // yanlış parçayı ölçerdi.
            _partCache.Clear();
            _measuresDirty = true;

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
                EditorGUILayout.HelpBox(
                    "Play kipinde kavrama yazılmaz — çalışan oyunda elin gerçeği sentetik eldir.",
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
                    "Silahın tanımı (WeaponDefinition) atanmamış — bake yazacak asset bulamaz.",
                    MessageType.Warning);
            }

            EditorGUILayout.Space();

            DrawHandSection(definition);
            EditorGUILayout.Space();
            DrawBakedSection(definition);
            EditorGUILayout.Space();
            DrawMissingPartsSection(definition);
            DrawGhostSourceSection();

            EditorGUILayout.HelpBox(
                "Akış: El Ekle → eli kabzaya oturt, parmakları hiyerarşiden bük → Bake. " +
                "Yazan tek düğme Bake'tir; \"Bake çıktısı\" bölümü salt okunurdur.",
                MessageType.None);
        }

        // ---------------------------------------------------------------- el modeli (bake kaynağı)

        /// <summary>
        /// Kavramanın <b>elle yazılan tek kaynağı</b>: silahın üstüne oturtulan el modeli.
        /// </summary>
        private void DrawHandSection(WeaponDefinition definition)
        {
            EditorGUILayout.LabelField("El modeli (kavramanın kaynağı)", EditorStyles.boldLabel);

            DrawHandRow(GripSocketKind.Primary, definition);
            if (definition != null && definition.IsTwoHanded)
            {
                DrawHandRow(GripSocketKind.Secondary, definition);
            }

            EditorGUILayout.LabelField(
                "Yalnız SAĞ el yazılır; sol el bake sırasında aynalanır.",
                EditorStyles.miniLabel);
        }

        private void DrawHandRow(GripSocketKind kind, WeaponDefinition definition)
        {
            Transform node = ItemHandRig.Find(_target.transform, kind);

            using (new EditorGUILayout.HorizontalScope())
            {
                string state = node == null
                    ? "el yok"
                    : (node.gameObject.activeSelf ? "düzenlemede" : "bake'lenmiş (gizli)");
                EditorGUILayout.LabelField($"{kind}", state, GUILayout.MinWidth(150f));

                if (node == null)
                {
                    using (new EditorGUI.DisabledScope(definition == null))
                    {
                        if (GUILayout.Button("El Ekle"))
                        {
                            AddHandNode(kind, definition);
                        }
                    }

                    return;
                }

                if (GUILayout.Button(node.gameObject.activeSelf ? "Gizle" : "Göster"))
                {
                    Undo.RecordObject(node.gameObject, "VortexArena El Görünürlüğü");
                    node.gameObject.SetActive(!node.gameObject.activeSelf);
                    MarkDirty(_target);
                    _measuresDirty = true;
                    SceneView.RepaintAll();
                }

                if (GUILayout.Button("Seç"))
                {
                    Selection.activeGameObject = node.gameObject;
                }

                using (new EditorGUI.DisabledScope(definition == null))
                {
                    if (GUILayout.Button("Bake"))
                    {
                        BakeHand(kind, definition);
                    }
                }
            }
        }

        /// <summary>
        /// El modelini silahın altına kalıcı olarak koyar.
        /// <para>⚠️ <b>Mevcut kavrama değerinden TOHUMLANIR</b>
        /// (<see cref="ItemHandGripBake.ToWristLocal"/>): bugüne kadar yazılmış silahların kavraması
        /// sıfırdan yazılmasın, el doğrudan doğru yerde belirsin. Aynı sebeple parmaklar da varsa
        /// mevcut poz düğümünden başlar.</para>
        /// <para>⚠️ Örnek <see cref="HideFlags.None"/> ile kurulur: prefaba KAYDEDİLİR. Geçici bir
        /// hayalet olsaydı pencere kapanınca kaybolur ve "yarın devam ederim" mümkün olmazdı.</para>
        /// </summary>
        private void AddHandNode(GripSocketKind kind, WeaponDefinition definition)
        {
            if (!TryGetGhostProvider(out HandGhostProvider provider))
            {
                return;
            }

            Handedness handedness = ItemHandRig.AuthoredHandIsRight ? Handedness.Right : Handedness.Left;
            HandGhost prototype = provider.GetHand(handedness);
            if (prototype == null)
            {
                Debug.LogWarning($"{LOG} El sağlayıcısında {handedness} el yok — el eklenemedi.", _target);
                return;
            }

            Transform handsRoot = EnsureHandsRoot(_target.transform);

            HandGhost instance = Instantiate(prototype, handsRoot);
            instance.gameObject.name = ItemHandRig.NodeName(kind);
            instance.gameObject.hideFlags = HideFlags.None;

            // ⚠️ localPosition'a YAZILMAZ, DÜNYA pozu yazılır. Kavrama ofsetleri metre cinsindendir
            // ama `Hands` düğümü ölçekli bir kökün (WPN_* kökleri 0.8) altında duruyor: yerel
            // konuma yazmak ofseti o ölçekle çarpar, el yanlış yere oturur ve bake onu geri
            // okuyunca tanımdaki sayı sessizce küçülür. Bileşim ItemHandGripBake.FromWrist'in
            // birebir tersidir — "El Ekle → hiç dokunmadan Bake" değeri DEĞİŞTİRMEMELİDİR.
            ItemHandGripBake.ToWristLocal(definition, kind,
                out Vector3 localPosition, out Quaternion localRotation);
            Transform itemRoot = _target.transform;
            instance.transform.SetPositionAndRotation(
                itemRoot.position + itemRoot.rotation * localPosition,
                itemRoot.rotation * localRotation);
            NormalizeWorldScale(instance.transform);

            HandGrabPose existing = ItemGripPoses.Find(
                _target.transform, kind, ItemHandRig.AuthoredHandIsRight);
            HandPose startPose = existing != null ? existing.HandPose : DefaultHandPose(handedness);
            if (startPose != null)
            {
                instance.SetPose(startPose,
                    new Pose(instance.transform.position, instance.transform.rotation));
            }

            Undo.RegisterCreatedObjectUndo(instance.gameObject, "VortexArena El Ekle");
            MarkDirty(_target);
            Selection.activeGameObject = instance.gameObject;
            _measuresDirty = true;
            SceneView.RepaintAll();

            Debug.Log($"{LOG} '{_target.name}' altına {ItemHandRig.RootNodeName}/" +
                      $"{ItemHandRig.NodeName(kind)} eklendi ve mevcut kavramadan konumlandırıldı. " +
                      "Eli/parmakları ayarlayıp Bake'e bas.", instance.gameObject);
        }

        /// <summary>
        /// El modelinden kavramayı üretir: bilek → tanım alanları, parmaklar → poz düğümü, sonra
        /// sola aynalar ve eli <b>gizler</b>.
        /// <para>⚠️ Gizleme adımı atlanamaz: el düğümü açık kalırsa arenada havada duran bir el
        /// olarak görünür (raftaki silah, kavrama tezgâhı, uzak avatarın eli). Runtime tarafında
        /// ayrıca emniyet var (<see cref="ItemHandRig.HideAll"/>) ama o son savunma hattıdır.</para>
        /// <para>⚠️ Model SİLİNMEZ, yalnız kapatılır — sonra beğenilmezse "Göster" ile geri
        /// düzenlenebilmeli.</para>
        /// </summary>
        private void BakeHand(GripSocketKind kind, WeaponDefinition definition)
        {
            Transform node = ItemHandRig.Find(_target.transform, kind);
            if (node == null)
            {
                return;
            }

            // ⚠️ Bilek = düğümün KENDİ transformu: HandGhost'un HandPuppet'ı aynı GameObject'tedir
            // (RequireComponent) ve HandPuppet.SetRootPose kendi transformunu bileğe yazıyor.
            // Alt ağaçtaki ilk puppet'ı almak, prefabda ikinci bir görsel varyant durduğunda
            // sessizce yanlış iskeleti okurdu.
            HandPuppet puppet = node.GetComponent<HandPuppet>();
            if (puppet == null || puppet.JointMaps == null)
            {
                Debug.LogError($"{LOG} '{node.name}' üstünde HandPuppet yok — parmak rotasyonları " +
                               "okunamaz, bake yapılmadı.", node);
                return;
            }

            bool rightHand = ItemHandRig.AuthoredHandIsRight;
            Handedness handedness = rightHand ? Handedness.Right : Handedness.Left;

            // 1) Bilek → tanım. ⚠️ Uzay yönü kavrama noktasına göre değişir (ana elde ters
            //    bileşim, ikincilde değil); tek uygulaması ItemHandGripBake'tedir.
            ItemHandGripBake.FromWrist(_target.transform, node, kind,
                out Vector3 gripPosition, out Vector3 gripEuler);

            var so = new SerializedObject(definition);
            so.FindProperty(kind == GripSocketKind.Primary
                ? "primaryGripPosition"
                : "secondaryGripPosition").vector3Value = gripPosition;
            so.FindProperty(kind == GripSocketKind.Primary
                ? "primaryGripEuler"
                : "secondaryGripEuler").vector3Value = gripEuler;
            so.ApplyModifiedProperties();

            // 2) Parmaklar → poz düğümü (yazılan el).
            HandGrabPose target = EnsurePoseNode(_target, kind, rightHand);
            Undo.RecordObject(target, "VortexArena Kavrama Bake");
            Undo.RecordObject(target.transform, "VortexArena Kavrama Bake");

            target.transform.SetPositionAndRotation(node.position, node.rotation);
            if (target.RelativeTo == null)
            {
                target.InjectRelativeTo(_target.transform);
            }

            HandPose pose = target.HandPose ?? new HandPose(handedness);
            WriteJointRotations(puppet, pose);
            target.InjectOptionalHandPose(pose);

            // 3) Karşı el — ayna matematiği ISDK'nın kendi API'sinden gelir.
            MirrorToOppositeHand(kind, rightHand);

            // 4) Gizle.
            Undo.RecordObject(node.gameObject, "VortexArena Kavrama Bake");
            node.gameObject.SetActive(false);

            MarkDirty(_target);
            _measuresDirty = true;
            SceneView.RepaintAll();

            Debug.Log($"{LOG} '{_target.name}' {kind} kavraması el modelinden yazıldı " +
                      $"(tanım + {ItemGripPoses.NodeName(kind, rightHand)} + ayna) ve el gizlendi. " +
                      "Yeniden düzenlemek için Göster'e bas.", _target);
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

        /// <summary>
        /// El modelinin DÜNYA ölçeğini 1'e sabitler (ebeveynin ölçeğini tersler).
        /// <para>
        /// ⚠️ <b>Bu adım aracın işe yaramasının şartıdır.</b> <c>WPN_*</c> köklerinin ölçeği 1
        /// değildir (bugün 0.8) ve el o kökün altında duruyor — ölçek tersellenmezse el de 0.8'e
        /// iner. Oysa oyunda oyuncunun eli <b>gerçek boyuttadır</b>, silah ise 0.8'dir: yani ekranda
        /// gördüğün "avuç kabzayı sarıyor mu" cevabı, gözlükte göreceğinden %25 farklı bir orandan
        /// verilirdi. Aracın tek işi o soruyu doğru cevaplatmak olduğu için bu telafi kozmetik
        /// değildir.
        /// </para>
        /// <para>Bake bundan etkilenmez (kavrama ofseti dünya metresi, parmak pozu yerel rotasyon —
        /// ikisi de ölçekten bağımsız); etkilenen şey yalnız GÖZÜN gördüğüdür.</para>
        /// </summary>
        private static void NormalizeWorldScale(Transform node)
        {
            Transform parent = node.parent;
            Vector3 parentScale = parent != null ? parent.lossyScale : Vector3.one;

            node.localScale = new Vector3(
                Mathf.Approximately(parentScale.x, 0f) ? 1f : 1f / parentScale.x,
                Mathf.Approximately(parentScale.y, 0f) ? 1f : 1f / parentScale.y,
                Mathf.Approximately(parentScale.z, 0f) ? 1f : 1f / parentScale.z);
        }

        private static Transform EnsureHandsRoot(Transform itemRoot)
        {
            Transform root = itemRoot.Find(ItemHandRig.RootNodeName);
            if (root != null)
            {
                return root;
            }

            var go = new GameObject(ItemHandRig.RootNodeName);
            Undo.RegisterCreatedObjectUndo(go, "VortexArena El Kökü");
            go.transform.SetParent(itemRoot, false);
            return go.transform;
        }

        // ------------------------------------------------------------------------ bake çıktısı

        /// <summary>
        /// Bake'in ürettiği veriyi <b>salt okunur</b> gösterir.
        /// <para>⚠️ Buraya düzenleme düğmesi konmaz. Poz düğümüne elle yazılan her şey bir sonraki
        /// bake'te üzerine yazılır; düğme koymak kullanıcıyı kaybolacak bir işe davet ederdi.</para>
        /// </summary>
        private void DrawBakedSection(WeaponDefinition definition)
        {
            EditorGUILayout.LabelField("Bake çıktısı (salt okunur)", EditorStyles.boldLabel);

            DrawBakedRow(GripSocketKind.Primary);
            if (definition != null && definition.IsTwoHanded)
            {
                DrawBakedRow(GripSocketKind.Secondary);
            }
        }

        private void DrawBakedRow(GripSocketKind kind)
        {
            HandGrabPose right = FindPoseNode(_target, kind, true);
            HandGrabPose left = FindPoseNode(_target, kind, false);

            // Eldeki ilk gerçek düğüm aynı zamanda el dalının sondasıdır (bkz. TryMeasureHandBranch).
            // ⚠️ `??` KULLANILMAZ: Unity'nin yok edilmiş nesne için özelleştirdiği `==` operatörünü
            // atlar ve ölü bir referansı "dolu" sayardı.
            TryMeasureHandBranch(right != null ? right : left);

            string state = right == null && left == null
                ? "hiç bake edilmedi"
                : $"sağ: {(right != null ? "var" : "yok")} · sol (ayna): {(left != null ? "var" : "yok")}";

            EditorGUILayout.LabelField($"{ItemGripPoses.NodeName(kind, true)}…", state);
        }

        // ---------------------------------------------------------------------- poz düğümleri

        /// <summary>
        /// Adı <see cref="ItemGripPoses"/>'den gelen poz düğümü; <b>poz verisi taşımasa da</b> döner.
        /// <para>⚠️ <see cref="ItemGripPoses.Find"/> kullanılmaz: o, tüketici tarafı için
        /// <c>UsesHandPose</c> false olan düğümü "yok" sayar — bake'in ise yarım kalmış bir düğümü
        /// <b>bulup</b> üzerine yazması gerekir, yoksa her koşuda ikinci bir düğüm üretilirdi.</para>
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
        /// hesaplamaya başlar ve bugünkü kavrama hissi (collider mesafesi) sessizce değişirdi. Bu
        /// düğümler saf VERİDİR; onları yalnız <c>HandGripPoser</c> okur.
        /// </para>
        /// <para>
        /// ⚠️ Düğüm <see cref="HandGrabUtils.CreateHandGrabPose"/> ile üretilir ve o yalnız bir
        /// GameObject + bileşen kurar. Düğümün ALTINDA el görünüyorsa kaynağı ISDK'nın <b>inspector</b>
        /// editörüdür (düğüm seçiliyken geçici hayalet örnekler) — prefaba yazılmaz, seçim
        /// kalkınca yok olur.
        /// </para>
        /// </summary>
        private HandGrabPose EnsurePoseNode(Weapon weapon, GripSocketKind kind, bool rightHand)
        {
            HandGrabPose existing = FindPoseNode(weapon, kind, rightHand);
            if (existing != null)
            {
                return existing;
            }

            Transform poseRoot = EnsurePoseRoot(weapon);

            HandGrabPose node = HandGrabUtils.CreateHandGrabPose(poseRoot, weapon.transform);
            node.gameObject.name = ItemGripPoses.NodeName(kind, rightHand);
            node.InjectOptionalHandPose(CreateDefaultHandPose(rightHand ? Handedness.Right : Handedness.Left));

            Undo.RegisterCreatedObjectUndo(node.gameObject, "VortexArena Kavrama Pozu");
            MarkDirty(weapon);
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
        /// Yazılan elin pozunu karşı ele aynalar — silah öteki ele geçtiğinde de bir karşılığı olsun
        /// diye.
        /// <para>⚠️ Ayna matematiği ELLE YAZILMAZ: ISDK'nın kendi
        /// <see cref="HandGrabUtils.MirrorHandGrabPose"/>'u kullanılır. İki eli ayrı ayrı yazmak aynı
        /// kavramanın iki kez tarif edilmesi olurdu ve ikisi zamanla kaçınılmaz olarak saparadı.</para>
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

            HandGrabPose mirror = EnsurePoseNode(_target, kind, !rightHand);

            Undo.RecordObject(mirror.transform, "VortexArena Kavrama Pozu Aynala");
            Undo.RecordObject(mirror, "VortexArena Kavrama Pozu Aynala");
            HandGrabUtils.MirrorHandGrabPose(source, mirror, _target.transform);
            MarkDirty(_target);
        }

        /// <summary>
        /// ⚠️ Prefab kipinde <see cref="EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.Scene)"/>
        /// işe yaramaz — prefab stage'in önizleme sahnesi kaydedilmez, kaydedilen ASSET'tir. Bu yüzden
        /// stage varken kirletilen kök objedir.
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
                return;
            }

            HandPose live = probe.HandPose;
            if (live == null)
            {
                return; // sonda geçersiz — ÖLÇÜLMEDİ sayılır, bir sonraki düğümde tekrar denenir
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
            if (EditorApplication.isPlaying || _target == null)
            {
                return;
            }

            if (_measuresDirty)
            {
                RebuildMeasures();
            }

            DrawMeasurements();
        }

        private void RebuildMeasures()
        {
            _measuresDirty = false;
            _measures.Clear();

            if (_target == null)
            {
                return;
            }

            AddMeasure(GripSocketKind.Primary);

            WeaponDefinition definition = _target.Definition;
            if (definition != null && definition.IsTwoHanded)
            {
                AddMeasure(GripSocketKind.Secondary);
            }
        }

        /// <summary>
        /// Bir el modelinin ölçüm noktalarını rig'inden çözer.
        /// <para>⚠️ Parmak <b>uçları</b> puppet'ın eklem tablosunda olmayabilir
        /// (<c>FingersMetadata.HAND_JOINT_IDS</c> uçları taşımaz) — bu yüzden son boğumun
        /// (<c>HandIndex3</c>) çocuğuna, o da yoksa ada göre aramaya düşülür. Hiçbiri tutmazsa ölçü
        /// sessizce çizilmez: eksik bir gösterge için hata basmak, aracın asıl işini gürültüye
        /// boğardı.</para>
        /// </summary>
        private void AddMeasure(GripSocketKind kind)
        {
            Transform node = ItemHandRig.Find(_target.transform, kind);
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
        /// Ölçüleri çizer.
        /// <para>⚠️ <b>Ölçülerin hedefi kavrama noktası DEĞİL silahın gerçek geometrisidir.</b> El
        /// modelinin bileği zaten kavrama noktasının üstünde duruyor; oraya olan her mesafe tanımı
        /// gereği neredeyse sabittir (ölçülen şey elin kendi boyu olur, kavramanın kalitesi değil).
        /// Bu yüzden hedef, ada göre bulunan kabza/ön kabza/tetik düğümüdür.</para>
        /// <para>⚠️ Yüzey mesafesi <see cref="Renderer.bounds"/> (dünya AABB) üzerinden alınır, yani
        /// mesh'e tam oturmayan bir YAKLAŞIMDIR. Kabza ve tetik gibi küçük parçalarda kutu yeterince
        /// dardır; sayı zaten mutlak bir değer için değil, el sürüklenirken <b>değişimi izlemek</b>
        /// için var.</para>
        /// <para>⚠️ Hedef parça bulunamazsa ölçü <b>hiç çizilmez</b>; eksiklik pencerede yazılı
        /// (<see cref="DrawMissingPartsSection"/>).</para>
        /// </summary>
        private void DrawMeasurements()
        {
            GUIStyle style = LabelStyle();

            for (int i = 0; i < _measures.Count; i++)
            {
                HandMeasure measure = _measures[i];

                if (measure.Node == null)
                {
                    // El silinmiş: liste bayat, bir sonraki karede yeniden kurulur.
                    _measuresDirty = true;
                    continue;
                }

                // Bake'lenmiş (gizli) el ölçülmez: ekranda görünmeyen bir şeyin yanına sayı yazmak
                // kullanıcıya hâlâ düzenlenebilir bir el varmış izlenimi verirdi.
                if (!measure.Node.gameObject.activeInHierarchy)
                {
                    continue;
                }

                Handles.color = Color.white;

                Renderer gripPart = FindWeaponPart(
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

                Renderer trigger = FindWeaponPart(TRIGGER_KEYS);
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

            // İki kavrama noktası arası eksen: iki elli çözümün silahı hizaladığı eksen budur
            // (ItemGripSolver ana noktaya oturur, ön kabzaya bakar). Eller gizliyken de çizilir,
            // çünkü kaynağı tanımın kendisidir.
            if (TryGetGripWorldPose(_target, GripSocketKind.Primary, out Pose primary) &&
                TryGetGripWorldPose(_target, GripSocketKind.Secondary, out Pose secondary))
            {
                Handles.color = Color.cyan;
                Handles.DrawLine(primary.position, secondary.position);
                Handles.Label(
                    Vector3.Lerp(primary.position, secondary.position, 0.5f),
                    $"kavrama ekseni: {Centimeters(primary.position, secondary.position)}",
                    style);
            }
        }

        /// <summary>
        /// Tanımdaki kavrama noktasının DÜNYA pozu.
        /// <para>⚠️ <c>TransformPoint</c> KULLANILMAZ: kavrama ofseti metre cinsindendir ve araya
        /// giren transformların ölçeği ona bulaşmamalı (aynı gerekçe <c>Weapon.ApplyCanonicalGrip</c>
        /// ve <c>ItemGripSockets.PrimarySocketWorld</c> içinde de elle bileşim yaptırıyor).</para>
        /// <para>⚠️ Primary <b>ters</b> okunur: SO'daki <c>primaryGrip</c> "el → eşya" yönündedir.
        /// Secondary zaten eşya-yereldir ve düz okunur (asimetrinin anlatımı
        /// <see cref="ItemDefinition"/> başındadır).</para>
        /// </summary>
        private static bool TryGetGripWorldPose(Weapon weapon, GripSocketKind kind, out Pose pose)
        {
            pose = default;

            ItemDefinition definition = weapon == null ? null : weapon.Definition;
            if (definition == null)
            {
                return false;
            }

            Vector3 localPosition;
            Quaternion localRotation;

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

        private static string GripPartLabel(GripSocketKind kind)
        {
            return kind == GripSocketKind.Primary ? "kabza" : "ön kabza";
        }

        /// <summary>
        /// Hedef silahın alt ağacında, adında anahtarlardan biri geçen ilk <see cref="Renderer"/>.
        /// Anahtarlar <b>sırayla</b> denenir, ilk tutan kazanır (spesifik olan listede önce durur).
        /// <para>⚠️ Sonuç — bulunamama dahil — önbelleğe alınır: bu yol <c>duringSceneGui</c>'den her
        /// karede çağrılıyor.</para>
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
        /// Ölçüye girmemesi gereken dallar: kavrama pozu düğümleri, <b>el modelleri</b>, kavrama
        /// çerçevesi ve editörün geçici objeleri.
        /// <para>⚠️ Bu eleme olmadan araç <b>kendi çizdiği şeyi ölçerdi</b> — el modelinin ve çerçeve
        /// prefabının altında da Renderer var ve adları aranan anahtarlarla çakışabiliyor.</para>
        /// </summary>
        private static bool IsMeasurementNoise(Transform node)
        {
            if (node.name == ItemGripPoses.RootNodeName || node.name == ItemHandRig.RootNodeName)
            {
                return true;
            }

            if (node.GetComponent<WeaponFrame>() != null)
            {
                return true;
            }

            // ISDK'nın inspector'ı seçili poz düğümünün altına geçici hayalet örnekliyor; o dal
            // sahneye yazılmaz ama ölçüm sırasında ortadadır.
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
