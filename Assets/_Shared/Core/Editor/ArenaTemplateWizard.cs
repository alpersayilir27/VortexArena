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
    /// sahneyi kopyalar, <c>MapDefinition</c> asset'i yazar, <c>GameCatalog</c>'a ve Build
    /// Settings'e ekler.
    /// <para>
    /// <b>Sihirbaz arena GEOMETRİSİNE DOKUNMAZ — ölçekleme yoktur.</b> Sahne kaynak arenadan
    /// (varsayılan 10×10) bire bir kopyalanır ve duvar/zemin/taban/işaretçi yerleşimi olduğu
    /// gibi gelir. Sebebi ürün gerçeği: her işletmenin alanı farklı ölçüde ve çoğu kare ya da
    /// dikdörtgen bile değil, yani arena planı her kurulumda zaten baştan çiziliyor. Orantılı
    /// ölçekleme bu durumda işe yarar bir taslak üretmiyor, yalnız elle düzeltilmesi gereken
    /// bir yalancı-doğru üretiyordu (ve zemin/işaretçi hiyerarşisine bağımlı kırılgan bir kod
    /// yığınıydı).
    /// </para>
    /// <para>
    /// Sihirbazın kattığı değer <b>bileşen bütünlüğü</b>: kopyalanan sahne ağa bağlanmak için
    /// gereken her şeyi hazır taşır (<c>ArenaBoundary</c>, <c>ArenaCalibrator</c> + işaretçiler,
    /// <c>PlayerPoseTracker</c>, <c>RemotePlayerSpawner</c>, <c>ModeHudSpawner</c>,
    /// <c>BaseZone</c>'lar, BB Camera Rig) — hiçbiri elle kurulmaz.
    /// </para>
    /// <para>
    /// <b>ELDE kalan işler</b> (sonuç uyarıları hatırlatır): arena geometrisini kendi planına
    /// göre çiz · <c>ArenaBoundary.halfExtentX/Z</c> + <c>MapDefinition.size</c> değerlerini
    /// gerçek ölçüye getir · kalibrasyon işaretçilerini zemin bandına göre yerleştir ·
    /// tek <see cref="SpawnPoint"/>'i <c>GameObject &gt; VortexArena &gt; Spawn Point</c> ile
    /// koy (sihirbaz ÜRETMEZ) · NavMesh/ışık bake et.
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
            EditorGUILayout.HelpBox(
                "Sihirbaz arena geometrisini ÖLÇEKLEMEZ — sahne kaynak arenadan bire bir kopyalanır. " +
                "Planı kendin çizip ArenaBoundary ve MapDefinition.size değerlerini gerçek ölçüye " +
                "getireceksin.",
                MessageType.Info);

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

            // ------------------------------------------------------- 4) sahneyi aç
            // Geometriye dokunulmaz (ölçekleme yok); sahne yalnız üzerinde çalışılsın diye açılır.
            Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            if (!scene.IsValid())
            {
                Fail(result, $"Kopyalanan sahne açılamadı: '{scenePath}'.");
                return;
            }

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

            result.Warnings.Add(
                "Arena geometrisi ÖLÇEKLENMEDİ — duvar/zemin/taban yerleşimi kaynak arenadan bire bir geldi. " +
                "Planı kendi alanına göre çiz; sonra ArenaBoundary.halfExtentX/Z ve MapDefinition.size " +
                "değerlerini gerçek ölçüye getir.");
            result.Warnings.Add(
                "Kalibrasyon işaretçilerini (anchor_a/anchor_b) zemin bandına göre yerleştir ve aralarındaki " +
                "mesafeyi not et (Docs/Isletme-Kurulum.md §3).");
            result.Warnings.Add("Başlangıç noktası ÜRETİLMEZ: 'GameObject > VortexArena > Spawn Point' ile tek SpawnPoint'i elle koy.");
            result.Warnings.Add("NavMesh ve ışık verisi kaynak sahneden MİRAS kalır — yeni plana göre yeniden bake edilmeli.");
            result.Warnings.Add("Ölçü değiştiyse 'Tools > VortexArena > Export Server Config' çalıştır (maps.json tazelensin).");

            result.Success = true;
            result.Summary = $"Arena '{arenaId}' üretildi: {sceneName} → {targetFolder}";
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
            // Boyut da kaynaktan gelir: sahne geometrisi ölçeklenmediği için MapDefinition'ın
            // kaynak arenayla aynı ölçüyü göstermesi tutarlıdır. Plan çizilince ikisi birlikte
            // elle güncellenir (sonuç uyarısı hatırlatır).
            Vector2 size = new Vector2(10f, 10f);
            if (!string.IsNullOrEmpty(options.sourceMapPath))
            {
                var sourceMap = AssetDatabase.LoadAssetAtPath<MapDefinition>(options.sourceMapPath);
                if (sourceMap != null)
                {
                    size = sourceMap.Size;
                    if (sourceMap.SupportedModeIds != null)
                    {
                        supportedModeIds = (string[])sourceMap.SupportedModeIds.Clone();
                    }
                }
                else
                {
                    result.Warnings.Add(
                        $"Kaynak MapDefinition bulunamadı ('{options.sourceMapPath}') — supportedModeIds boş " +
                        "bırakıldı (kısıtsız), boyut 10×10 yazıldı.");
                }
            }

            var map = ScriptableObject.CreateInstance<MapDefinition>();
            AssetDatabase.CreateAsset(map, assetPath);

            var mapObject = new SerializedObject(map);
            mapObject.FindProperty("sceneName").stringValue = sceneName;
            mapObject.FindProperty("displayName").stringValue = string.IsNullOrWhiteSpace(options.displayName)
                ? sceneName
                : options.displayName.Trim();
            mapObject.FindProperty("size").vector2Value = size;

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
