using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VortexArena.Core.Arena;

namespace VortexArena.Core.Editor
{
    /// <summary>
    /// <c>Tools &gt; VortexArena &gt; Create Arena From Template</c> — mevcut bir arenayı
    /// şablon alarak yeni bir arena kutusu (<c>{Scenes, Data, Prefabs}</c>) üretir:
    /// sahneyi kopyalar, YENİ BOYUTA ÖLÇEKLER, <c>MapDefinition</c> asset'i yazar,
    /// <c>GameCatalog</c>'a ve Build Settings'e ekler.
    /// <para>
    /// <b>Ölçekleme özeti</b> (bakımcı için — sihirbazın sahnede yaptığı TAM liste):
    /// <list type="number">
    /// <item><b>Çerçeve:</b> <c>ArenaBoundary</c> taşıyan transform ARENA ÇERÇEVESİDİR
    /// (arena merkezi + Y rotasyonu); onun parent'ı arena köküdür. Tüm hesaplar bu çerçevenin
    /// yerel uzayında yapılır.</item>
    /// <item><b>Konum geçişi (shear'sız):</b> arena kökünün altındaki her transform için
    /// hedef dünya konumu ÖNCE hesaplanır
    /// (<c>p = frame.InverseTransformPoint(pos)</c> → <c>(p.x*rx, p.y, p.z*rz)</c> → dünyaya geri),
    /// SONRA hiyerarşi sırasıyla (üstten alta) uygulanır. Rotasyon/ölçek dokunulmaz.
    /// Arena kökünün KENDİSİ oynatılmaz (çocukların dünya konumu zaten yeniden yazıldığı için
    /// kökü kaydırmak yalnız yerel offsetleri bozardı); çerçeve ve grup kökleri arena
    /// merkezinde olduğu için formül onları zaten yerinde bırakır.</item>
    /// <item><b>Duvarlar:</b> adı "Wall" ile başlayan ve <c>Renderer</c> taşıyan objelerde
    /// yerel Y rotasyonu 0/180'e yakınsa <c>localScale.x *= rx</c>, 90/270'e yakınsa
    /// <c>localScale.x *= rz</c> (şablonda duvar meshleri X ekseninde uzanır).</item>
    /// <item><b>Zemin:</b> <c>Ground</c> grubunun altındaki <c>GroundMesh</c> objesi
    /// <c>x*=rx, z*=rz</c> ölçeklenir; yoksa <c>Ground</c> grubunun kendisi ölçeklenir ve
    /// "zemin çocukları eğrilebilir" uyarısı döner.</item>
    /// <item><b>BaseZone:</b> <c>halfExtentZ *= rz</c> (<c>halfExtentX</c> = şerit genişliği,
    /// DEĞİŞMEZ); zone arena-yerel X'i duvara yaslanır
    /// (<c>x = sign(eskiX) * (sizeX/2 - halfExtentX)</c>); görsel şerit çocuğu
    /// <c>localScale.z *= rz</c>.</item>
    /// <item><b>SpawnPoint'ler:</b> SIFIRDAN yerleştirilir — eksikse sonuncusu klonlanır,
    /// fazlaysa silinir; i. nokta arena-yerel <c>z = -halfZ + (i+1) * (2*halfZ/(n+1))</c>,
    /// X = zone'un yeni X'i; <c>slot = i</c>, <c>team</c> = BaseZone'un takımı,
    /// bakış yönü arena merkezine (yalnız Y ekseni) çevrilir.</item>
    /// <item><b>ArenaBoundary:</b> <c>halfExtentX = sizeX/2</c>, <c>halfExtentZ = sizeZ/2</c>.</item>
    /// </list>
    /// Duvar/cover yerleşimi KABA kalır — sanat geçişi elde yapılır.
    /// </para>
    /// <para>
    /// <b>⚠️ Katalog tuzağı:</b> <c>GameCatalog.MapsForMode</c>, bir <c>ModeDefinition</c>'ın
    /// <c>maps</c> dizisi DOLUYSA yalnız o listeyi tarar. Yeni harita bu açık listelere
    /// eklenmezse admin harita seçicisinde GÖRÜNMEZ — bu yüzden sihirbaz, haritayı destekleyen
    /// her modun dolu <c>maps</c> dizisine yeni haritayı da ekler.
    /// </para>
    /// </summary>
    public class ArenaTemplateWizard : EditorWindow
    {
        private const string StandardRoot = "Assets/Arenas/Standard";
        private const string VenuesRoot = "Assets/Arenas/Venues";

        [SerializeField] private ArenaTemplateOptions options = new ArenaTemplateOptions();

        // Sonuç BİLEREK serialize edilmiyor: Unity, null [Serializable] alanları domain reload
        // sonrası boş bir örnekle doldurur ve pencere sahte bir "HATA" kutusu gösterirdi.
        [NonSerialized] private ArenaTemplateResult lastResult;

        private Vector2 scroll;

        /// <summary>Menü girişi — sihirbaz penceresini açar.</summary>
        [MenuItem("Tools/VortexArena/Create Arena From Template")]
        private static void Open()
        {
            var window = GetWindow<ArenaTemplateWizard>(true, "Arena Şablon Sihirbazı", true);
            window.minSize = new Vector2(470f, 420f);
            window.Show();
        }

        /// <summary>
        /// arenaId'den sahne adı önerir: "A" + rakam ile başlıyorsa <c>"Arena" + arenaId.Substring(1)</c>
        /// (A12x12 → Arena12x12), değilse <c>"Arena" + arenaId</c>.
        /// </summary>
        public static string SuggestSceneName(string arenaId)
        {
            if (string.IsNullOrWhiteSpace(arenaId))
            {
                return string.Empty;
            }

            string trimmed = arenaId.Trim();
            if (trimmed.Length >= 2 && (trimmed[0] == 'A' || trimmed[0] == 'a') && char.IsDigit(trimmed[1]))
            {
                return "Arena" + trimmed.Substring(1);
            }

            return "Arena" + trimmed;
        }

        // ------------------------------------------------------------- pencere

        private void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);

            EditorGUILayout.LabelField("Kaynak", EditorStyles.boldLabel);
            options.sourceScenePath = AssetPathField<SceneAsset>("Kaynak sahne", options.sourceScenePath);
            options.sourceMapPath = AssetPathField<MapDefinition>("Kaynak MapDefinition", options.sourceMapPath);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Yeni arena", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            string arenaId = EditorGUILayout.TextField(
                new GUIContent("Arena Id (klasör)", "Kutu klasörü + MapDefinition asset adı, ör. A12x12"),
                options.arenaId);
            if (EditorGUI.EndChangeCheck())
            {
                // Sahne adı elle değiştirilmediyse arenaId ile birlikte güncellensin.
                if (string.IsNullOrEmpty(options.sceneName) ||
                    string.Equals(options.sceneName, SuggestSceneName(options.arenaId), StringComparison.Ordinal))
                {
                    options.sceneName = SuggestSceneName(arenaId);
                }

                options.arenaId = arenaId;
            }

            options.sceneName = EditorGUILayout.TextField(
                new GUIContent("Sahne adı (katalog anahtarı)", "start_match.sceneName ile BİREBİR aynı olmalı"),
                options.sceneName);
            options.displayName = EditorGUILayout.TextField("Gösterim adı", options.displayName);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Fiziksel alan", EditorStyles.boldLabel);
            options.sizeX = EditorGUILayout.FloatField("Genişlik X (m)", options.sizeX);
            options.sizeZ = EditorGUILayout.FloatField("Derinlik Z (m)", options.sizeZ);
            options.spawnSlotsPerTeam = EditorGUILayout.IntField("Takım başına spawn", options.spawnSlotsPerTeam);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Hedef", EditorStyles.boldLabel);
            options.target = (ArenaTemplateTarget)EditorGUILayout.EnumPopup("Kutu", options.target);
            if (options.target == ArenaTemplateTarget.Venue)
            {
                options.venueName = EditorGUILayout.TextField("İşletme adı (klasör)", options.venueName);
            }

            options.catalogPath = AssetPathField<GameCatalog>("GameCatalog", options.catalogPath);
            EditorGUILayout.HelpBox("Hedef klasör: " + ResolveTargetFolder(options), MessageType.None);

            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(options.arenaId)))
            {
                if (GUILayout.Button("Oluştur", GUILayout.Height(28f)))
                {
                    // Dialog YALNIZ pencereden çağrıldığında; Create() kendisi dialogsuzdur.
                    if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                    {
                        lastResult = Create(options);
                        if (lastResult.Success)
                        {
                            Debug.Log($"[CreateArena] {lastResult.Summary}");
                        }
                        else
                        {
                            Debug.LogError($"[CreateArena] {lastResult.Error}");
                        }
                    }
                }
            }

            DrawResult();
            EditorGUILayout.EndScrollView();
        }

        /// <summary>Son çalıştırmanın özetini/uyarılarını gösterir.</summary>
        private void DrawResult()
        {
            if (lastResult == null)
            {
                return;
            }

            EditorGUILayout.Space();
            if (!lastResult.Success)
            {
                EditorGUILayout.HelpBox("HATA: " + lastResult.Error, MessageType.Error);
                return;
            }

            EditorGUILayout.HelpBox(lastResult.Summary, MessageType.Info);
            if (lastResult.Warnings == null)
            {
                return;
            }

            for (int i = 0; i < lastResult.Warnings.Count; i++)
            {
                EditorGUILayout.HelpBox(lastResult.Warnings[i], MessageType.Warning);
            }
        }

        /// <summary>Asset yolunu ObjectField olarak çizer; seçim değişirse yeni yolu döner.</summary>
        private static string AssetPathField<T>(string label, string path) where T : UnityEngine.Object
        {
            T current = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<T>(path);
            T picked = EditorGUILayout.ObjectField(label, current, typeof(T), false) as T;
            if (ReferenceEquals(picked, current))
            {
                return path;
            }

            return ReferenceEquals(picked, null) ? string.Empty : AssetDatabase.GetAssetPath(picked);
        }

        // -------------------------------------------------------------- üretim

        /// <summary>
        /// Yeni arena kutusunu üretir. HİÇBİR dialog açmaz ve exception fırlatmaz —
        /// hata durumunda <see cref="ArenaTemplateResult.Success"/> <c>false</c> döner.
        /// </summary>
        /// <param name="options">Kaynak + yeni arena parametreleri.</param>
        /// <returns>Üretilen yollar, özet ve elle rötuş uyarıları.</returns>
        public static ArenaTemplateResult Create(ArenaTemplateOptions options)
        {
            var result = new ArenaTemplateResult();

            try
            {
                CreateInternal(options, result);
            }
            catch (Exception exception)
            {
                result.Success = false;
                result.Error = "Beklenmeyen hata: " + exception;
            }

            return result;
        }

        private static void CreateInternal(ArenaTemplateOptions options, ArenaTemplateResult result)
        {
            // ---------------------------------------------------- 1) doğrulama
            if (options == null)
            {
                Fail(result, "options null.");
                return;
            }

            string arenaId = (options.arenaId ?? string.Empty).Trim();
            string sceneName = string.IsNullOrWhiteSpace(options.sceneName)
                ? SuggestSceneName(arenaId)
                : options.sceneName.Trim();

            if (!IsValidFileName(arenaId))
            {
                Fail(result, $"Geçersiz arenaId: '{options.arenaId}'.");
                return;
            }

            if (!IsValidFileName(sceneName))
            {
                Fail(result, $"Geçersiz sahne adı: '{sceneName}'.");
                return;
            }

            if (options.sizeX <= 0f || options.sizeZ <= 0f)
            {
                Fail(result, $"Arena boyutu pozitif olmalı (sizeX={options.sizeX}, sizeZ={options.sizeZ}).");
                return;
            }

            if (options.spawnSlotsPerTeam < 1)
            {
                Fail(result, $"spawnSlotsPerTeam en az 1 olmalı (verilen: {options.spawnSlotsPerTeam}).");
                return;
            }

            if (options.target == ArenaTemplateTarget.Venue && !IsValidFileName(options.venueName))
            {
                Fail(result, $"Venue hedefi için geçerli bir işletme adı gerekli (verilen: '{options.venueName}').");
                return;
            }

            string sourceScenePath = options.sourceScenePath;
            if (string.IsNullOrEmpty(sourceScenePath) || AssetDatabase.LoadAssetAtPath<SceneAsset>(sourceScenePath) == null)
            {
                Fail(result, $"Kaynak sahne bulunamadı: '{sourceScenePath}'.");
                return;
            }

            string targetFolder = ResolveTargetFolder(options);
            if (AssetDatabase.IsValidFolder(targetFolder) || Directory.Exists(targetFolder))
            {
                Fail(result, $"Hedef klasör ZATEN VAR: '{targetFolder}' (üzerine yazılmaz).");
                return;
            }

            if (IsSceneInBuildSettings(sceneName))
            {
                Fail(result, $"'{sceneName}' Build Settings'te zaten kayıtlı — sahne adı katalog anahtarıdır, benzersiz olmalı.");
                return;
            }

            if (SceneAssetExistsElsewhere(sceneName))
            {
                result.Warnings.Add($"Projede '{sceneName}' adlı başka bir sahne dosyası var — katalog anahtarı çakışabilir.");
            }

            // ---------------------------------------------------- 2) klasörler
            if (!EnsureFolder(targetFolder) ||
                !EnsureFolder(targetFolder + "/Scenes") ||
                !EnsureFolder(targetFolder + "/Data") ||
                !EnsureFolder(targetFolder + "/Prefabs"))
            {
                Fail(result, $"Klasör yapısı oluşturulamadı: '{targetFolder}'.");
                return;
            }

            // ---------------------------------------------- 3) sahneyi kopyala
            string scenePath = $"{targetFolder}/Scenes/{sceneName}.unity";
            if (!AssetDatabase.CopyAsset(sourceScenePath, scenePath))
            {
                Fail(result, $"Sahne kopyalanamadı: '{sourceScenePath}' → '{scenePath}'.");
                return;
            }

            result.ScenePath = scenePath;

            // ------------------------------------------------ 4) aç + ölçekle
            Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            if (!scene.IsValid())
            {
                Fail(result, $"Kopyalanan sahne açılamadı: '{scenePath}'.");
                return;
            }

            if (!ScaleArena(options.sizeX, options.sizeZ, options.spawnSlotsPerTeam, result))
            {
                // Klasör + sahne kopyası oluşmuş durumda; tekrar denemeden önce elle temizlensin
                // (açık sahnenin asset'ini kod içinden silmek güvenli değil).
                result.Error += $" — yarım kalan kutu elle silinmeli: '{targetFolder}'.";
                return;
            }

            // ------------------------------------------- 5) sahneyi kaydet
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            // ------------------------------------------- 6) MapDefinition asset
            string mapAssetPath = $"{targetFolder}/Data/{arenaId}.asset";
            MapDefinition map = CreateMapDefinition(mapAssetPath, sceneName, options, result);
            result.MapAssetPath = mapAssetPath;

            // ------------------------------------------------------ 7) katalog
            RegisterInCatalog(options.catalogPath, map, result);

            // ------------------------------------------------ 8) build settings
            var buildScenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes)
            {
                new EditorBuildSettingsScene(scenePath, true)
            };
            EditorBuildSettings.scenes = buildScenes.ToArray();

            // --------------------------------------------------------- 9) kayıt
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            result.Warnings.Add("Duvar/cover yerleşimi KABA — sanat geçişi (kapak objeleri, materyaller, ölçek rötuşu) elle yapılmalı.");
            result.Warnings.Add("NavMesh ve ışık verisi kaynak sahneden MİRAS kalır — yeni boyutta yeniden bake edilmeli.");

            result.Success = true;
            result.Summary =
                $"Arena '{arenaId}' üretildi: {sceneName} ({options.sizeX:0.###}×{options.sizeZ:0.###} m, " +
                $"takım başına {options.spawnSlotsPerTeam} spawn) → {targetFolder}";
        }

        /// <summary>Hedef kutu klasörü: Standard/&lt;arenaId&gt; veya Venues/&lt;venueName&gt;.</summary>
        private static string ResolveTargetFolder(ArenaTemplateOptions options)
        {
            if (options == null)
            {
                return string.Empty;
            }

            return options.target == ArenaTemplateTarget.Venue
                ? $"{VenuesRoot}/{(options.venueName ?? string.Empty).Trim()}"
                : $"{StandardRoot}/{(options.arenaId ?? string.Empty).Trim()}";
        }

        // ------------------------------------------------------------ ölçekleme

        /// <summary>
        /// Açık sahnedeki arenayı yeni boyuta ölçekler (sınıf başlığındaki adım listesi).
        /// Başarısızsa <paramref name="result"/> hata ile doldurulur ve <c>false</c> döner.
        /// </summary>
        private static bool ScaleArena(float sizeX, float sizeZ, int spawnSlotsPerTeam, ArenaTemplateResult result)
        {
            var boundary = UnityEngine.Object.FindFirstObjectByType<ArenaBoundary>(FindObjectsInactive.Include);
            if (boundary == null)
            {
                Fail(result, "Sahnede ArenaBoundary yok — arena çerçevesi bulunamadı.");
                return false;
            }

            Transform frame = boundary.transform;
            Transform arenaRoot = frame.parent;
            if (arenaRoot == null)
            {
                Fail(result, "ArenaBoundary'nin parent'ı yok — arena kökü (PlayArea) bekleniyordu.");
                return false;
            }

            var boundaryObject = new SerializedObject(boundary);
            SerializedProperty halfXProp = boundaryObject.FindProperty("halfExtentX");
            SerializedProperty halfZProp = boundaryObject.FindProperty("halfExtentZ");
            if (halfXProp == null || halfZProp == null)
            {
                Fail(result, "ArenaBoundary.halfExtentX/halfExtentZ alanları okunamadı.");
                return false;
            }

            float oldSizeX = halfXProp.floatValue * 2f;
            float oldSizeZ = halfZProp.floatValue * 2f;
            if (oldSizeX <= 0.0001f || oldSizeZ <= 0.0001f)
            {
                Fail(result, $"Kaynak arena boyutu geçersiz ({oldSizeX}×{oldSizeZ}).");
                return false;
            }

            float rx = sizeX / oldSizeX;
            float rz = sizeZ / oldSizeZ;

            RepositionSubtree(arenaRoot, frame, rx, rz);
            ScaleWalls(arenaRoot, frame, rx, rz);
            ScaleGround(arenaRoot, rx, rz, result);
            ScaleBases(arenaRoot, frame, sizeX, sizeZ, rz, spawnSlotsPerTeam, result);

            halfXProp.floatValue = sizeX * 0.5f;
            halfZProp.floatValue = sizeZ * 0.5f;
            boundaryObject.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(boundary);

            return true;
        }

        /// <summary>
        /// Shear'sız konum geçişi: hedef dünya konumları ÖNCE hesaplanır, sonra hiyerarşi
        /// sırasıyla uygulanır. Arena kökünün kendisi yerinde bırakılır.
        /// </summary>
        private static void RepositionSubtree(Transform arenaRoot, Transform frame, float rx, float rz)
        {
            Transform[] all = arenaRoot.GetComponentsInChildren<Transform>(true);
            var targets = new Vector3[all.Length];

            for (int i = 0; i < all.Length; i++)
            {
                Vector3 local = frame.InverseTransformPoint(all[i].position);
                targets[i] = frame.TransformPoint(new Vector3(local.x * rx, local.y, local.z * rz));
            }

            // GetComponentsInChildren hiyerarşi sırası döner (kök → çocuk): üstten alta uygula.
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] == arenaRoot)
                {
                    continue;
                }

                all[i].position = targets[i];
                EditorUtility.SetDirty(all[i]);
            }
        }

        /// <summary>Duvar meshlerini uzandıkları eksene göre genişletir.</summary>
        private static void ScaleWalls(Transform arenaRoot, Transform frame, float rx, float rz)
        {
            Transform[] all = arenaRoot.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                Transform wall = all[i];
                if (wall == frame || wall == arenaRoot)
                {
                    continue;
                }

                if (!wall.name.StartsWith("Wall", StringComparison.Ordinal) || wall.GetComponent<Renderer>() == null)
                {
                    continue;
                }

                Vector3 scale = wall.localScale;
                scale.x *= IsAlongArenaX(wall.localEulerAngles.y) ? rx : rz;
                wall.localScale = scale;
                EditorUtility.SetDirty(wall);
            }
        }

        /// <summary>Y rotasyonu 0/180'e yakın mı (duvar arena X ekseni boyunca mı uzanıyor).</summary>
        private static bool IsAlongArenaX(float yawDegrees)
        {
            float folded = Mathf.Repeat(yawDegrees, 180f);
            return folded < 45f || folded > 135f;
        }

        /// <summary>Zemini ölçekler: önce <c>GroundMesh</c>, yoksa <c>Ground</c> grubunun kendisi.</summary>
        private static void ScaleGround(Transform arenaRoot, float rx, float rz, ArenaTemplateResult result)
        {
            Transform ground = arenaRoot.Find("Ground");
            if (ground == null)
            {
                result.Warnings.Add("Sahnede 'Ground' grubu bulunamadı — zemin ölçeklenmedi.");
                return;
            }

            Transform groundMesh = FindDescendant(ground, "GroundMesh");
            if (groundMesh != null)
            {
                Vector3 scale = groundMesh.localScale;
                scale.x *= rx;
                scale.z *= rz;
                groundMesh.localScale = scale;
                EditorUtility.SetDirty(groundMesh);
                return;
            }

            if (ground.GetComponent<MeshRenderer>() != null)
            {
                Vector3 scale = ground.localScale;
                scale.x *= rx;
                scale.z *= rz;
                ground.localScale = scale;
                EditorUtility.SetDirty(ground);
                result.Warnings.Add("Zemin çocukları eğrilebilir (GroundMesh alt objesi yok, 'Ground' grubunun kendisi ölçeklendi).");
                return;
            }

            result.Warnings.Add("'Ground' grubunda MeshRenderer yok ve GroundMesh alt objesi bulunamadı — zemin ölçeklenmedi.");
        }

        /// <summary>Tüm BaseZone'ları duvara yaslar, şeritleri ve spawn noktalarını yeniden kurar.</summary>
        private static void ScaleBases(
            Transform arenaRoot,
            Transform frame,
            float sizeX,
            float sizeZ,
            float rz,
            int spawnSlotsPerTeam,
            ArenaTemplateResult result)
        {
            BaseZone[] zones = arenaRoot.GetComponentsInChildren<BaseZone>(true);
            if (zones.Length == 0)
            {
                result.Warnings.Add("Sahnede BaseZone bulunamadı — taban/spawn yerleşimi atlandı.");
                return;
            }

            for (int i = 0; i < zones.Length; i++)
            {
                BaseZone zone = zones[i];
                var zoneObject = new SerializedObject(zone);
                SerializedProperty halfXProp = zoneObject.FindProperty("halfExtentX");
                SerializedProperty halfZProp = zoneObject.FindProperty("halfExtentZ");
                SerializedProperty teamProp = zoneObject.FindProperty("team");

                float halfExtentX = halfXProp != null ? halfXProp.floatValue : 0.5f;
                if (halfZProp != null)
                {
                    halfZProp.floatValue *= rz;
                }

                zoneObject.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(zone);

                Team team = teamProp != null ? (Team)teamProp.enumValueIndex : Team.Red;

                // Zone'u yeni duvara yasla (şerit genişliği kadar içeride).
                Vector3 zoneLocal = frame.InverseTransformPoint(zone.transform.position);
                zoneLocal.x = Mathf.Sign(zoneLocal.x) * (sizeX * 0.5f - halfExtentX);
                zone.transform.position = frame.TransformPoint(zoneLocal);
                EditorUtility.SetDirty(zone.transform);

                ScaleBaseStrip(zone, rz);
                RebuildSpawnPoints(frame, zone, team, zoneLocal.x, spawnSlotsPerTeam, sizeZ, result);
            }
        }

        /// <summary>BaseZone'un görsel şeridini (Renderer taşıyan doğrudan çocuk) derinlemesine uzatır.</summary>
        private static void ScaleBaseStrip(BaseZone zone, float rz)
        {
            for (int i = 0; i < zone.transform.childCount; i++)
            {
                Transform child = zone.transform.GetChild(i);
                if (child.GetComponent<SpawnPoint>() != null || child.GetComponent<Renderer>() == null)
                {
                    continue;
                }

                Vector3 scale = child.localScale;
                scale.z *= rz;
                child.localScale = scale;
                EditorUtility.SetDirty(child);
            }
        }

        /// <summary>
        /// Takımın spawn noktalarını SIFIRDAN yerleştirir: sayıyı <paramref name="count"/>'a
        /// getirir (klonla/sil), kenar boyunca eşit aralıklarla dizer, <c>slot</c>/<c>team</c>
        /// alanlarını yazar ve arena merkezine baktırır.
        /// </summary>
        private static void RebuildSpawnPoints(
            Transform frame,
            BaseZone zone,
            Team team,
            float zoneLocalX,
            int count,
            float sizeZ,
            ArenaTemplateResult result)
        {
            var points = new List<SpawnPoint>(zone.GetComponentsInChildren<SpawnPoint>(true));
            if (points.Count == 0)
            {
                result.Warnings.Add($"{team} tabanında hiç SpawnPoint yok — klonlanacak şablon bulunamadı, spawn yerleşimi atlandı.");
                return;
            }

            while (points.Count > count)
            {
                SpawnPoint extra = points[points.Count - 1];
                points.RemoveAt(points.Count - 1);
                UnityEngine.Object.DestroyImmediate(extra.gameObject);
            }

            while (points.Count < count)
            {
                SpawnPoint template = points[points.Count - 1];
                var clone = UnityEngine.Object.Instantiate(template.gameObject, template.transform.parent);
                points.Add(clone.GetComponent<SpawnPoint>());
            }

            float halfZ = sizeZ * 0.5f;
            float step = 2f * halfZ / (count + 1);

            for (int i = 0; i < points.Count; i++)
            {
                SpawnPoint point = points[i];
                point.gameObject.name = $"Spawn_{team}_{i}";

                Vector3 local = frame.InverseTransformPoint(point.transform.position);
                local.x = zoneLocalX;
                local.z = -halfZ + (i + 1) * step;
                point.transform.position = frame.TransformPoint(local);

                // Bakış yönü arena merkezine (yalnız Y ekseni).
                Vector3 toCenter = frame.position - point.transform.position;
                toCenter.y = 0f;
                if (toCenter.sqrMagnitude > 0.0001f)
                {
                    point.transform.rotation = Quaternion.LookRotation(toCenter.normalized, Vector3.up);
                }

                var pointObject = new SerializedObject(point);
                SerializedProperty slotProp = pointObject.FindProperty("slot");
                SerializedProperty teamProp = pointObject.FindProperty("team");
                if (slotProp != null)
                {
                    slotProp.intValue = i;
                }

                if (teamProp != null)
                {
                    teamProp.enumValueIndex = (int)team;
                }

                pointObject.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(point);
                EditorUtility.SetDirty(point.transform);
            }
        }

        // ----------------------------------------------------- asset + katalog

        /// <summary>
        /// Yeni <c>MapDefinition</c> asset'ini yazar. Alanlar <c>private [SerializeField]</c>
        /// olduğu için <see cref="SerializedObject"/> üzerinden doldurulur;
        /// <c>supportedModeIds</c> kaynak haritadan kopyalanır (kaynak yoksa boş = kısıtsız).
        /// </summary>
        private static MapDefinition CreateMapDefinition(
            string assetPath,
            string sceneName,
            ArenaTemplateOptions options,
            ArenaTemplateResult result)
        {
            string[] supportedModeIds = Array.Empty<string>();
            if (!string.IsNullOrEmpty(options.sourceMapPath))
            {
                var sourceMap = AssetDatabase.LoadAssetAtPath<MapDefinition>(options.sourceMapPath);
                if (sourceMap != null && sourceMap.SupportedModeIds != null)
                {
                    supportedModeIds = (string[])sourceMap.SupportedModeIds.Clone();
                }
                else if (sourceMap == null)
                {
                    result.Warnings.Add($"Kaynak MapDefinition bulunamadı ('{options.sourceMapPath}') — supportedModeIds boş bırakıldı (kısıtsız).");
                }
            }

            var map = ScriptableObject.CreateInstance<MapDefinition>();
            AssetDatabase.CreateAsset(map, assetPath);

            var mapObject = new SerializedObject(map);
            mapObject.FindProperty("sceneName").stringValue = sceneName;
            mapObject.FindProperty("displayName").stringValue = string.IsNullOrWhiteSpace(options.displayName)
                ? sceneName
                : options.displayName.Trim();
            mapObject.FindProperty("size").vector2Value = new Vector2(options.sizeX, options.sizeZ);
            mapObject.FindProperty("spawnSlotsPerTeam").intValue = options.spawnSlotsPerTeam;

            SerializedProperty modesProp = mapObject.FindProperty("supportedModeIds");
            modesProp.arraySize = supportedModeIds.Length;
            for (int i = 0; i < supportedModeIds.Length; i++)
            {
                modesProp.GetArrayElementAtIndex(i).stringValue = supportedModeIds[i];
            }

            mapObject.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(map);
            return map;
        }

        /// <summary>
        /// Haritayı <c>GameCatalog.maps</c>'e ekler ve haritayı destekleyen her modun DOLU
        /// <c>maps</c> dizisine de ekler — aksi hâlde açık liste yeni haritayı gizler
        /// (bkz. sınıf başlığındaki katalog tuzağı).
        /// </summary>
        private static void RegisterInCatalog(string catalogPath, MapDefinition map, ArenaTemplateResult result)
        {
            if (string.IsNullOrEmpty(catalogPath))
            {
                result.Warnings.Add("GameCatalog yolu boş — harita kataloğa EKLENMEDİ (admin seçicisinde görünmez).");
                return;
            }

            var catalog = AssetDatabase.LoadAssetAtPath<GameCatalog>(catalogPath);
            if (catalog == null)
            {
                result.Warnings.Add($"GameCatalog bulunamadı ('{catalogPath}') — harita kataloğa EKLENMEDİ.");
                return;
            }

            var catalogObject = new SerializedObject(catalog);
            AppendUnique(catalogObject.FindProperty("maps"), map);
            catalogObject.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(catalog);

            ModeDefinition[] modes = catalog.Modes;
            if (modes == null)
            {
                return;
            }

            for (int i = 0; i < modes.Length; i++)
            {
                ModeDefinition mode = modes[i];
                if (mode == null || mode.Maps == null || mode.Maps.Length == 0)
                {
                    // Boş liste = "katalogdaki tüm uyumlu haritalar" — dokunmaya gerek yok.
                    continue;
                }

                if (!map.SupportsMode(mode.ModeId))
                {
                    continue;
                }

                var modeObject = new SerializedObject(mode);
                AppendUnique(modeObject.FindProperty("maps"), map);
                modeObject.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(mode);
            }
        }

        /// <summary>Diziye referansı sonuna ekler (zaten varsa dokunmaz).</summary>
        private static void AppendUnique(SerializedProperty arrayProp, UnityEngine.Object value)
        {
            if (arrayProp == null || !arrayProp.isArray)
            {
                return;
            }

            for (int i = 0; i < arrayProp.arraySize; i++)
            {
                if (arrayProp.GetArrayElementAtIndex(i).objectReferenceValue == value)
                {
                    return;
                }
            }

            arrayProp.arraySize++;
            arrayProp.GetArrayElementAtIndex(arrayProp.arraySize - 1).objectReferenceValue = value;
        }

        // ------------------------------------------------------------ yardımcı

        /// <summary>Ada göre alt ağaçta ilk eşleşen transform (kökün kendisi hariç).</summary>
        private static Transform FindDescendant(Transform root, string name)
        {
            Transform[] all = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != root && string.Equals(all[i].name, name, StringComparison.Ordinal))
                {
                    return all[i];
                }
            }

            return null;
        }

        /// <summary>Klasör zincirini (gerekirse üstten aşağı) oluşturur.</summary>
        private static bool EnsureFolder(string folderPath)
        {
            if (string.IsNullOrEmpty(folderPath) || AssetDatabase.IsValidFolder(folderPath))
            {
                return true;
            }

            string parent = Path.GetDirectoryName(folderPath)?.Replace('\\', '/');
            string leaf = Path.GetFileName(folderPath);
            if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(leaf))
            {
                return false;
            }

            if (!EnsureFolder(parent))
            {
                return false;
            }

            return !string.IsNullOrEmpty(AssetDatabase.CreateFolder(parent, leaf));
        }

        /// <summary>Sahne adı Build Settings listesinde var mı (enabled fark etmez).</summary>
        private static bool IsSceneInBuildSettings(string sceneName)
        {
            EditorBuildSettingsScene[] scenes = EditorBuildSettings.scenes;
            for (int i = 0; i < scenes.Length; i++)
            {
                if (scenes[i] == null || string.IsNullOrEmpty(scenes[i].path))
                {
                    continue;
                }

                if (string.Equals(Path.GetFileNameWithoutExtension(scenes[i].path), sceneName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Projede aynı adlı bir sahne asset'i var mı (katalog anahtarı çakışması).</summary>
        private static bool SceneAssetExistsElsewhere(string sceneName)
        {
            string[] guids = AssetDatabase.FindAssets($"t:SceneAsset {sceneName}");
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (string.Equals(Path.GetFileNameWithoutExtension(path), sceneName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Dosya/klasör adı olarak kullanılabilir mi.</summary>
        private static bool IsValidFileName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            return value.Trim().IndexOfAny(Path.GetInvalidFileNameChars()) < 0;
        }

        /// <summary>Sonucu hata olarak işaretler (exception ATILMAZ).</summary>
        private static void Fail(ArenaTemplateResult result, string error)
        {
            result.Success = false;
            result.Error = error;
        }
    }
}
