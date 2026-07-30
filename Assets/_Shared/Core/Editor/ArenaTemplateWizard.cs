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
    /// <b>Arena ölçüsünün tek temsili boyut JSON'udur</b> (<c>ArenaDimensions</c>) ve bir geometri
    /// kaynağı seçmek <b>ZORUNLUDUR</b>: ölçüsüz bir arenanın <c>ArenaBoundary</c>'si devre dışı
    /// kalır, yani arena sessizce sınırsız olurdu. İki kaynak vardır ve <b>tek boru hattında</b>
    /// buluşurlar:
    /// <list type="bullet">
    /// <item><c>DimensionsJson</c> — elle yazılan boyut dosyası (şeritmetreyle alınan ölçü).</item>
    /// <item><c>TestMesh</c> — kaba blok yığını; <see cref="ArenaTestMeshBuilder"/> onu bir plana
    /// ÇIKARIR, planı yeni arena kutusunun <c>Data/</c> klasörüne JSON olarak YAZAR ve oradan
    /// sonrası birinci yolla birebir aynıdır.</item>
    /// </list>
    /// Yani her iki yolda da diskte bir boyut dosyası oluşur ve
    /// <c>ArenaBoundary.dimensionsJson</c> her zaman DOLU bağlanır.
    /// </para>
    /// <para>
    /// <b>Bu ÖLÇEKLEME değildir</b> — sihirbaz hiçbir geometriyi büyütüp küçültmez. Sahne kaynak
    /// arenadan kopyalanır, şablonun hazır zemin/duvar mesh'leri silinir ve yerine kaynaktaki
    /// GERÇEK ölçüden geometri üretilir. Orantılı ölçekleme bilinçli olarak yoktur: her işletmenin
    /// alanı farklı ölçüde ve çoğu kare ya da dikdörtgen bile değil, yani plan her kurulumda zaten
    /// baştan çiziliyor.
    /// </para>
    /// <para>
    /// <b>Üretilen her şey tek bir dalda toplanır:</b> zemin/duvar/kolon,
    /// <c>ArenaBoundary</c>'nin altındaki <c>ArenaGeometry</c> çocuğuna kurulur — kalibrasyon
    /// işaretçileri, taban bölgeleri ve rig ile karışmasın diye.
    /// </para>
    /// <para>
    /// Sihirbazın kattığı değer <b>bileşen bütünlüğü</b>: kopyalanan sahne ağa bağlanmak için
    /// gereken her şeyi hazır taşır (<c>ArenaBoundary</c>, <c>ArenaCalibrator</c> + işaretçiler,
    /// <c>PlayerPoseTracker</c>, <c>RemotePlayerSpawner</c>, <c>ModeHudSpawner</c>,
    /// <c>BaseZone</c>'lar, BB Camera Rig) — hiçbiri elle kurulmaz.
    /// </para>
    /// <para>
    /// <b>ELDE kalan işler</b> (sonuç uyarıları hatırlatır): taban bölgelerini yeni plana göre
    /// taşı · kalibrasyon işaretçilerini zemin bandına göre yerleştir ·
    /// tek <see cref="SpawnPoint"/>'i <c>GameObject &gt; VortexArena &gt; Spawn Point</c> ile
    /// koy (sihirbaz ÜRETMEZ) · NavMesh/ışık bake et.
    /// </para>
    /// <para>
    /// <b>⚠️ Katalog tuzağı:</b> <c>GameCatalog.MapsForMode</c>, bir <c>ModeDefinition</c>'ın
    /// <c>maps</c> dizisi DOLUYSA yalnız o listeyi tarar. Yeni harita bu açık listelere
    /// eklenmezse admin harita seçicisinde GÖRÜNMEZ — bu yüzden sihirbaz, haritayı destekleyen
    /// her modun dolu <c>maps</c> dizisine yeni haritayı da ekler.
    /// </para>
    /// <para>
    /// <b>Katalog seçtirilmez</b> — pencerede alanı yoktur: projedeki tek <c>GameCatalog</c>
    /// asset'i otomatik çözülür (<see cref="ResolveCatalog"/>), çünkü çalışma anında katalog
    /// yolla değil <c>Resources.Load</c> ile bulunuyor. Birden fazla katalog varsa kayıt
    /// YAPILMAZ ve hata basılır.
    /// </para>
    /// </summary>
    public class ArenaTemplateWizard : EditorWindow
    {
        private const string VenuesRoot = "Assets/Arenas/Venues";

        // Şablon sahnesindeki hazır geometrinin GERÇEK adları (Default12x12): plan verildiğinde
        // yalnız bunlar silinir — kalibrasyon işaretçileri ve taban bölgeleri korunur.
        private const string TemplateGroundMeshName = "GroundMesh";
        private const string TemplateWallPrefix = "Wall_";

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
            // Kaynak TEK seçilir ve yalnız ona ait alan çizilir: iki dolu alan "hangisinin
            // ölçüsü geçerli" sorusunu doğurur, o da ikinci bir doğruluk kaynağıdır.
            var pickedSource = (ArenaGeometrySource)EditorGUILayout.EnumPopup(
                new GUIContent("Geometri kaynağı",
                    "Arena ölçüsü nereden gelsin. İki yol da diske bir boyut JSON'u bırakır."),
                options.geometrySource);

            if (pickedSource != options.geometrySource)
            {
                // Seçim değişince ötekinin yolu TEMİZLENİR — kapalı bir alanda kalan yol,
                // sonradan geri dönüldüğünde sessizce eski kaynağı diriltirdi.
                if (pickedSource != ArenaGeometrySource.DimensionsJson)
                {
                    options.dimensionsJsonPath = string.Empty;
                }

                if (pickedSource != ArenaGeometrySource.TestMesh)
                {
                    options.testMeshPath = string.Empty;
                }
            }

            options.geometrySource = pickedSource;

            switch (options.geometrySource)
            {
                case ArenaGeometrySource.TestMesh:
                    options.testMeshPath = AssetPathField<GameObject>(
                        new GUIContent("TestMesh (prefab)",
                            "Alanı kabaca temsil eden blok yığınının kökü. Plana çıkarılıp arena " +
                            "kutusunun Data/ klasörüne JSON olarak yazılır."),
                        options.testMeshPath);
                    break;

                default:
                    options.dimensionsJsonPath = AssetPathField<TextAsset>(
                        new GUIContent("Boyut dosyası (JSON)",
                            "ArenaDimensions JSON'u: zemin sınırı + kolonlar (metre, arena yerel XZ)."),
                        options.dimensionsJsonPath);
                    break;
            }

            EditorGUILayout.HelpBox(GeometrySourceHelp(options), MessageType.Info);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Hedef", EditorStyles.boldLabel);
            options.venueName = EditorGUILayout.TextField(
                new GUIContent("Mekan (klasör)", "Arenanın oynanacağı işletme/mekan klasörü, ör. VortexAntep"),
                options.venueName);

            // GameCatalog SEÇTİRİLMEZ: projede tek bir katalog vardır ve çalışma anında yolla
            // değil `Resources.Load<GameCatalog>("GameCatalog")` ile bulunur — seçtirmek yalnız
            // yanlış asset'e yazma yolu açardı. Araç kataloğu kendi çözer (RegisterInCatalog).
            EditorGUILayout.HelpBox("Hedef klasör: " + ResolveTargetFolder(options), MessageType.None);

            EditorGUILayout.Space();
            // Kaynak alanı da zorunlu: ölçüsüz üretilen arenanın ArenaBoundary'si devre dışı kalır
            // ve arena sessizce sınırsız olur — bunu düğmeyi kapatarak baştan engelliyoruz.
            using (new EditorGUI.DisabledScope(
                       string.IsNullOrWhiteSpace(options.arenaId) ||
                       string.IsNullOrWhiteSpace(options.venueName) ||
                       string.IsNullOrWhiteSpace(options.SourcePath())))
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

        /// <summary>Seçili geometri kaynağının ne yapacağını anlatan kutu metni.</summary>
        private static string GeometrySourceHelp(ArenaTemplateOptions options)
        {
            if (options.geometrySource == ArenaGeometrySource.TestMesh)
            {
                return "TestMesh: bloklardan bir plan çıkarılıp arena kutusunun Data/ klasörüne " +
                       "'<sahneAdı>_dimensions.json' olarak YAZILACAK, geometri o dosyadan üretilecek ve " +
                       "ArenaBoundary ona bağlanacak. Ölçüyü sonradan dosyada düzeltip " +
                       "'Build Arena From Dimensions' ile yeniden çizebilirsin.";
            }

            return "Boyut dosyası: şablondan gelen zemin/duvar mesh'leri silinip JSON'daki plandan " +
                   "üretilecek, ArenaBoundary bu dosyaya ve üretilen duvarlara bağlanacak.";
        }

        /// <summary>Asset yolunu ObjectField olarak çizer; seçim değişirse yeni yolu döner.</summary>
        private static string AssetPathField<T>(string label, string path) where T : UnityEngine.Object
        {
            return AssetPathField<T>(new GUIContent(label), path);
        }

        /// <summary>İpucu metinli varyant (bkz. yukarıdaki özet).</summary>
        private static string AssetPathField<T>(GUIContent label, string path) where T : UnityEngine.Object
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

            // Mekan ZORUNLUDUR: oynanacak her arena bir mekana aittir ve mekanı klasör yolu belirler
            // (MapDefinition'da mekan alanı yoktur — CLAUDE.md). Mekansız bir arena export'ta
            // sahte bir mekan üretir ve sunucu açılışında operatöre yanlış liste gösterirdi.
            if (!IsValidFileName(options.venueName))
            {
                Fail(result, $"Geçerli bir mekan (işletme) klasör adı gerekli (verilen: '{options.venueName}').");
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
            // Yalnız DOLU olacak klasörler açılır. Boş bir Prefabs/Art klasörü git'te yaşayamaz
            // (git dosya tutar, klasör tutmaz) → klonda kaybolur, geriye yetim .meta kalır ve
            // Unity açılışta klasörü hayalet olarak geri üretir. Arenaya özel prefab/sanat
            // gerektiğinde klasörü elle açmak doğru olanıdır.
            if (!EnsureFolder(targetFolder) ||
                !EnsureFolder(targetFolder + "/Scenes") ||
                !EnsureFolder(targetFolder + "/Data"))
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

            // -------------------------------------------------- 5) geometri
            // Kaynak ZORUNLU; okunamazsa sahne şablondan olduğu gibi kalır ve uyarı düşer
            // (sihirbaz yarıda kesilmez, ama arena ölçüsüz kalır).
            bool geometryApplied = ApplyGeometry(options, scene, targetFolder, sceneName, result);

            // ------------------------------------------- 6) MapDefinition asset
            string mapAssetPath = $"{targetFolder}/Data/{arenaId}.asset";
            MapDefinition map = CreateMapDefinition(mapAssetPath, sceneName, options, result);
            result.MapAssetPath = mapAssetPath;

            // ------------------------------------------------------ 7) katalog
            RegisterInCatalog(map, result);

            // ------------------------------------------------ 8) build settings
            var buildScenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes)
            {
                new EditorBuildSettingsScene(scenePath, true)
            };
            EditorBuildSettings.scenes = buildScenes.ToArray();

            // --------------------------------------------------------- 9) kayıt
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            // Kaynak seçilip de üretilemediyse sebebi ApplyGeometry zaten uyarı olarak yazdı.
            if (geometryApplied)
            {
                result.Warnings.Add(
                    "Zemin/duvar/kolon boyut dosyasından üretildi (ArenaGeometry altında) ve " +
                    "ArenaBoundary.dimensionsJson bağlandı. Taban bölgeleri (Base_Red/Base_Blue) ve " +
                    "kalibrasyon işaretçileri şablondaki yerinde kaldı — yeni plana göre elle taşı.");
            }

            result.Warnings.Add(
                "Kalibrasyon işaretçilerini (anchor_a/anchor_b) zemin bandına göre yerleştir ve aralarındaki " +
                "mesafeyi not et (Docs/Isletme-Kurulum.md §3).");
            result.Warnings.Add("Başlangıç noktası ÜRETİLMEZ: 'GameObject > VortexArena > Spawn Point' ile tek SpawnPoint'i elle koy.");
            result.Warnings.Add("NavMesh ve ışık verisi kaynak sahneden MİRAS kalır — yeni plana göre yeniden bake edilmeli.");
            result.Warnings.Add(
                "'Tools > VortexArena > Export Server Config' çalıştır — yeni sceneName maps.json'a " +
                "girmezse start_match reddedilir.");

            result.Success = true;
            result.Summary = $"Arena '{arenaId}' üretildi: {sceneName} → {targetFolder}";
        }

        /// <summary>
        /// Hedef kutu klasörü — HER ZAMAN <c>Venues/&lt;venueName&gt;/&lt;arenaId&gt;</c>.
        /// <para>
        /// ⚠️ Mekansız ("standart") arena kutusu YOKTUR: oynanacak her arena bir mekana aittir ve
        /// mekanı yalnız klasör yolu söyler (<c>MapDefinition</c>'da mekan alanı yoktur). Mekan
        /// dışına üretilen bir arena export'ta sahte bir mekan doğurur ve sunucu açılışında
        /// operatöre var olmayan bir seçenek gösterirdi.
        /// </para>
        /// <para>
        /// ⚠️ İşletme klasörü bir arena DEĞİL, arena kutularının kabıdır: bir işletmede birden
        /// çok arena oynatılır ve hepsi o işletmenin <c>Default</c>'undan türetilir. Arena adı bu
        /// yüzden bir alt seviyededir — venue klasörünün kendisi kutu olsaydı ikinci arena için
        /// yer kalmazdı.
        /// </para>
        /// </summary>
        private static string ResolveTargetFolder(ArenaTemplateOptions options)
        {
            if (options == null)
            {
                return string.Empty;
            }

            string arenaId = (options.arenaId ?? string.Empty).Trim();

            return $"{VenuesRoot}/{(options.venueName ?? string.Empty).Trim()}/{arenaId}";
        }

        // ---------------------------------------------------------- arena planı

        /// <summary>
        /// Şablon sahnesindeki hazır zemin/duvar mesh'lerini seçilen kaynaktan üretilenle
        /// değiştirir ve sahnedeki <c>ArenaBoundary</c>'yi bağlar (boyut dosyası + duvarlar).
        /// <para>
        /// İki kaynak (<see cref="ArenaGeometrySource"/>) farklı yerden başlar ama AYNI noktada
        /// buluşur: ortada bir boyut JSON'u olur, geometri ondan üretilir ve
        /// <c>ArenaBoundary.dimensionsJson</c> ona bağlanır. TestMesh yolunun tek fazlası, JSON'u
        /// yeni arena kutusunun <c>Data/</c> klasörüne kendisinin yazmasıdır.
        /// </para>
        /// <para>
        /// ⚠️ <b>Silinecek objeler ada göre bulunur</b> — şablon sahnesinin gerçek hiyerarşisi:
        /// <c>PlayArea &gt; Ground &gt; GroundMesh</c> (zemin mesh'i) ve <c>ArenaBoundary</c>'nin
        /// KENDİ çocukları <c>Wall_N/S/E/W</c>. <c>Ground</c> objesinin kendisine dokunulmaz:
        /// kalibrasyon işaretçileri (<c>anchor_a</c>/<c>anchor_b</c>) onun altındadır, silinirse
        /// arena ağa hizalanamaz.
        /// </para>
        /// <para>
        /// ⚠️ Geometri <see cref="ArenaBoundary"/>'yi taşıyan transformun ALTINA üretilir: plan
        /// koordinatları o transformun yerel XZ düzlemindedir, başka bir ebeveyn planı kaydırırdı.
        /// </para>
        /// </summary>
        /// <returns>Geometri üretildiyse <c>true</c>; kaynak okunamadıysa <c>false</c>.</returns>
        private static bool ApplyGeometry(
            ArenaTemplateOptions options,
            Scene scene,
            string targetFolder,
            string sceneName,
            ArenaTemplateResult result)
        {
            var boundary = UnityEngine.Object.FindFirstObjectByType<ArenaBoundary>(FindObjectsInactive.Include);
            if (boundary == null)
            {
                result.Warnings.Add(
                    "Sahnede ArenaBoundary yok — ölçü uygulanamadı, geometri şablondan geldi. Arena SINIRSIZ.");
                return false;
            }

            // ------------------------------------------------- 1) kaynağı çöz
            // Kaynak okunamazsa sahneye HİÇ dokunulmaz: şablon geometrisini silip yerine bir şey
            // koyamamak, arenayı zeminsiz bırakırdı.
            TextAsset dimensionsAsset = null;
            GameObject testMesh = null;
            ArenaDimensions plan = null;

            if (options.geometrySource == ArenaGeometrySource.TestMesh)
            {
                testMesh = AssetDatabase.LoadAssetAtPath<GameObject>(options.testMeshPath);
                if (testMesh == null)
                {
                    result.Warnings.Add(
                        $"TestMesh bulunamadı ('{options.testMeshPath}') — geometri şablondan OLDUĞU GİBİ geldi, " +
                        "arena ÖLÇÜSÜZ kaldı.");
                    return false;
                }
            }
            else
            {
                dimensionsAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(options.dimensionsJsonPath);
                if (dimensionsAsset == null)
                {
                    result.Warnings.Add(
                        $"Boyut dosyası bulunamadı ('{options.dimensionsJsonPath}') — geometri şablondan " +
                        "OLDUĞU GİBİ geldi, arena ÖLÇÜSÜZ kaldı.");
                    return false;
                }

                plan = ArenaDimensions.FromTextAsset(dimensionsAsset, out string dimensionsError);
                if (plan == null)
                {
                    result.Warnings.Add(
                        $"Boyut dosyası okunamadı ('{options.dimensionsJsonPath}'): {dimensionsError} — " +
                        "geometri şablondan OLDUĞU GİBİ geldi, arena ÖLÇÜSÜZ kaldı.");
                    return false;
                }
            }

            // ---------------------------------------------------- 2) üretim
            Transform root = boundary.transform;
            RemoveTemplateGeometry(scene, root);

            List<MeshRenderer> walls;

            if (testMesh != null)
            {
                ArenaTestMeshBuilder.TestMeshResult extracted = ArenaTestMeshBuilder.BuildAndWrite(
                    testMesh, root, targetFolder + "/Data", sceneName, result.Warnings);

                if (!extracted.Success)
                {
                    result.Warnings.Add("TestMesh plana çevrilemedi: " + extracted.Error);
                    return false;
                }

                dimensionsAsset = extracted.DimensionsAsset;
                walls = extracted.Geometry.WallRenderers;
                result.Warnings.Add($"Boyut dosyası yazıldı: {extracted.JsonPath}");
            }
            else
            {
                ArenaShapeBuilder.Result built = ArenaShapeBuilder.Build(plan, root);
                if (!built.Success)
                {
                    result.Warnings.Add("Plan geometriye çevrilemedi: " + built.Error);
                    return false;
                }

                walls = built.WallRenderers;
            }

            // ------------------------------------------- 3) ArenaBoundary bağla
            ArenaShapeBuilder.BindBoundary(boundary, dimensionsAsset, walls, result.Warnings);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            return true;
        }

        /// <summary>
        /// Şablondan gelen zemin/duvar mesh'lerini siler (bkz. <see cref="ApplyGeometry"/> hiyerarşi
        /// notu). Bulunamayanlar sessizce atlanır: şablon zamanla değişebilir ve eksik bir ad
        /// yüzünden arena üretimini durdurmak orantısız olurdu.
        /// </summary>
        private static void RemoveTemplateGeometry(Scene scene, Transform boundaryRoot)
        {
            for (int i = boundaryRoot.childCount - 1; i >= 0; i--)
            {
                Transform child = boundaryRoot.GetChild(i);
                if (child.name.StartsWith(TemplateWallPrefix, StringComparison.Ordinal))
                {
                    UnityEngine.Object.DestroyImmediate(child.gameObject);
                }
            }

            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                Transform groundMesh = FindDescendant(roots[i].transform, TemplateGroundMeshName);
                if (groundMesh != null)
                {
                    UnityEngine.Object.DestroyImmediate(groundMesh.gameObject);
                }
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
                if (sourceMap == null)
                {
                    result.Warnings.Add(
                        $"Kaynak MapDefinition bulunamadı ('{options.sourceMapPath}') — supportedModeIds boş " +
                        "bırakıldı (kısıtsız).");
                }
                else if (sourceMap.SupportedModeIds != null)
                {
                    supportedModeIds = (string[])sourceMap.SupportedModeIds.Clone();
                }
            }

            var map = ScriptableObject.CreateInstance<MapDefinition>();
            AssetDatabase.CreateAsset(map, assetPath);

            var mapObject = new SerializedObject(map);
            mapObject.FindProperty("sceneName").stringValue = sceneName;
            mapObject.FindProperty("displayName").stringValue = string.IsNullOrWhiteSpace(options.displayName)
                ? sceneName
                : options.displayName.Trim();

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
        /// <para>
        /// Katalog PARAMETRE DEĞİLDİR, projeden çözülür: çalışma anında katalog
        /// <c>Resources.Load&lt;GameCatalog&gt;("GameCatalog")</c> ile bulunuyor, yani doğru olan
        /// tek bir asset var. Yol seçtirmek yalnız "hiçbir şeyin okumadığı bir kataloğa yazma"
        /// yolunu açardı.
        /// </para>
        /// </summary>
        private static void RegisterInCatalog(MapDefinition map, ArenaTemplateResult result)
        {
            GameCatalog catalog = ResolveCatalog(result);
            if (catalog == null)
            {
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

        /// <summary>
        /// Projedeki TEK <c>GameCatalog</c> asset'ini bulur; bulunamazsa ya da birden fazlaysa
        /// null döner (sebep sonuç uyarılarına yazılır).
        /// <para>
        /// Birden fazla katalog bir PROJE HATASIDIR: çalışma anında <c>Resources.Load</c> hangisini
        /// döndüreceğini garanti etmez, yani "hangisine yazmalıyım" sorusunun doğru cevabı yoktur.
        /// Bu durumda kayıt yapılmaz — yanlış kataloğa yazmak sessizce görünmeyen bir arena üretir.
        /// </para>
        /// </summary>
        private static GameCatalog ResolveCatalog(ArenaTemplateResult result)
        {
            string[] guids = AssetDatabase.FindAssets("t:" + nameof(GameCatalog));
            if (guids == null || guids.Length == 0)
            {
                result.Warnings.Add(
                    "Projede GameCatalog asset'i YOK — harita kataloğa EKLENMEDİ " +
                    "(admin mod/harita seçicisinde görünmez). Beklenen yer: " +
                    "Assets/_Shared/Data/Resources/GameCatalog.asset");
                return null;
            }

            if (guids.Length > 1)
            {
                var paths = new List<string>(guids.Length);
                for (int i = 0; i < guids.Length; i++)
                {
                    paths.Add(AssetDatabase.GUIDToAssetPath(guids[i]));
                }

                string message =
                    "Projede birden fazla GameCatalog asset'i var — harita hiçbirine EKLENMEDİ. " +
                    "Katalog çalışma anında Resources.Load ile bulunuyor, yani tek olmak ZORUNDA. " +
                    "Bulunanlar: " + string.Join(" · ", paths);
                Debug.LogError("[CreateArena] " + message);
                result.Warnings.Add(message);
                return null;
            }

            string catalogPath = AssetDatabase.GUIDToAssetPath(guids[0]);
            var catalog = AssetDatabase.LoadAssetAtPath<GameCatalog>(catalogPath);
            if (catalog == null)
            {
                result.Warnings.Add($"GameCatalog yüklenemedi ('{catalogPath}') — harita kataloğa EKLENMEDİ.");
                return null;
            }

            // Resources/ dışındaki katalog derlemeyi kırmaz ama çalışma anında HİÇ yüklenmez:
            // admin seçicisi ve mod HUD eşlemesi boş kalır. Kayıt yine de yapılır, uyarı düşer.
            if (catalogPath.IndexOf("/Resources/", StringComparison.OrdinalIgnoreCase) < 0)
            {
                result.Warnings.Add(
                    $"GameCatalog 'Resources/' altında DEĞİL ('{catalogPath}') — çalışma anında " +
                    "Resources.Load ile yüklenemez; harita kataloğa yazıldı ama admin listesinde görünmez.");
            }

            return catalog;
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
