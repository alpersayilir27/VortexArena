using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.ProBuilder;
using UnityEngine.ProBuilder.MeshOperations;
using VortexArena.Core.Arena;

namespace VortexArena.Core.Editor
{
    /// <summary>
    /// <c>Tools &gt; VortexArena &gt; Arena &gt; JSON'dan DimensionMesh Üret</c> — bir mekanın boyut
    /// dosyasını (<see cref="ArenaDimensions"/>) sahnedeki <b>ölçü maketine</b> çevirir:
    /// <c>&lt;Mekan&gt;_DimensionMesh</c> kökü altında tek bir <c>Plane</c> çokgeni, her kolon için
    /// bir prizma ve iki kalibrasyon işaretçisi (<c>anchor_a</c> / <c>anchor_b</c>).
    /// <para>
    /// <b>Maket oynanan geometri DEĞİLDİR ama build'e GİRER:</b> ürettiği <c>anchor_a</c> /
    /// <c>anchor_b</c> küpleri sahnenin kalibrasyon işaretçilerinin ta kendisidir ve çalışma
    /// anında <see cref="ArenaCalibrator"/> onları arar. Oyunda çizilen yalnız işaretçilerdir;
    /// taban ve kolon görselini <see cref="ArenaDimensionMesh"/> <c>Awake</c>'te kapatır. Arena
    /// sanatı maketin üstüne kurulur; duvar ÜRETİLMEZ (arenanın duvarları environment'a aittir).
    /// </para>
    /// <para>
    /// <b>Maket sahnedeki <see cref="ArenaBoundary"/>'nin ALTINA kurulur</b> — yerel konum/dönüş
    /// sıfır, ölçek 1. Böylece arenayı hazır bir environment'ın üstüne oturtmak <b>tek objeyi</b>
    /// taşımak/döndürmek olur (muhafaza örneği); maket ve altındaki kalibrasyon işaretçileri onu
    /// kendiliğinden izler, ikisi zaten çakışık olmak zorundadır (muhafazanın ölçüsü de aynı
    /// dosyadan gelir). Sahnede muhafaza yoksa maket sahne köküne, dünya orijininde ve dönüşsüz
    /// kurulur.
    /// </para>
    /// <para>
    /// ⚠️ <b>Maketin ölçeği değiştirilmez</b> ve döndürülmüş bir kökün altında ölçü <b>dünya
    /// eksenli kutuda okunmaz</b>: 12×12 bir taban, 48,72° dönmüş bir kökün altında seçim
    /// kutusunda <c>12 × (cos θ + sin θ)</c> = 16,93 görünür ve araç ölçeği bozuyor sanılır.
    /// Ölçünün doğru okunduğu yer daima boyut dosyasıdır; geri okuma da maketin KENDİ kökünü
    /// referans aldığı için taşınmış/döndürülmüş maket doğru çevrilir.
    /// </para>
    /// <para>
    /// ⚠️ <b>Idempotent:</b> sahnede aynı mekanın maketi varsa silinip yeniden üretilir; ikinci
    /// bir kopya birikmez. Maket dışındaki hiçbir objeye dokunulmaz.
    /// </para>
    /// <para>
    /// ⚠️ <c>EditorUtility.DisplayDialog</c> YOK: modal dialog Unity ana thread'ini kilitliyor ve
    /// CLI'dan çalıştırınca komut timeout veriyor. Sonuç <c>Debug.Log</c> ile bildirilir.
    /// </para>
    /// </summary>
    public class DimensionMeshBuilder : EditorWindow
    {
        private const string VenuesRoot = "Assets/Arenas/Venues";
        private const string SharedMaterialPath = "Assets/Materials/M_Mekan.mat";

        // Kalibrasyon işaretçileri takım malzemeleriyle boyanır: A kırmızı, B mavi. Yeni asset
        // üretmemek için mevcutlar kullanıldı — iki noktanın hangisi olduğunun BİR BAKIŞTA
        // ayrılması sıranın (A→B) kendisinden önemli, operatör yanan işaretçiden doğrular.
        private const string MarkAMaterialPath = "Assets/Materials/M_TeamRed.mat";
        private const string MarkBMaterialPath = "Assets/Materials/M_TeamBlue.mat";

        /// <summary>Kalibrasyon işaretçisi küpünün kenar uzunluğu (metre).</summary>
        private const float MarkSize = 0.12f;

        [SerializeField] private TextAsset dimensionsJson;

        /// <summary>Üretim sonucu — çağıran (pencere / başka araç) raporlamak için kullanır.</summary>
        public sealed class Result
        {
            /// <summary>Üretilen maketin kökü; başarısızlıkta null.</summary>
            public GameObject Root;

            /// <summary>Mekan adı (yoldan türetildi).</summary>
            public string VenueName;

            /// <summary>Üretilen kolon sayısı.</summary>
            public int ColumnCount;

            /// <summary>Taban halkasının köşe sayısı.</summary>
            public int PlanePointCount;

            /// <summary>
            /// Üretilen tabanın XZ ölçüsü (metre) = dosyadaki halkanın sınırlayıcı kutusu. Maket
            /// kendi kökünde dönüşsüz olduğu için maketin YEREL uzayında ölçülen değer de budur
            /// (kök döndürülmüşse dünya eksenli seçim kutusu daha büyük okunur — bkz. sınıf
            /// başlığı).
            /// </summary>
            public Vector2 PlaneLocalSize;

            /// <summary>Kalibrasyon işaretçileri üretildi mi (dosyada nokta varsa).</summary>
            public bool HasCalibration;

            /// <summary>Üretim gerçekleşti mi.</summary>
            public bool Success;

            /// <summary>Başarısızsa sebebi.</summary>
            public string Error;

            /// <summary>Kurtarılmış ama dikkat isteyen durumlar.</summary>
            public readonly List<string> Warnings = new List<string>();
        }

        // --------------------------------------------------------------- pencere

        [MenuItem("Tools/VortexArena/Arena/JSON'dan DimensionMesh Üret", false, 2)]
        private static void Open()
        {
            var window = GetWindow<DimensionMeshBuilder>(true, "Boyut Maketi Üret", true);
            window.minSize = new Vector2(430f, 210f);
            window.Show();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Kaynak", EditorStyles.boldLabel);
            dimensionsJson = (TextAsset)EditorGUILayout.ObjectField(
                new GUIContent("Boyut dosyası (JSON)",
                    "Mekanın ArenaDimensions JSON'u: taban halkası + kolonlar (metre, arena yerel XZ)."),
                dimensionsJson, typeof(TextAsset), false);

            if (dimensionsJson != null)
            {
                EditorGUILayout.HelpBox(
                    "Mekan: " + ResolveVenueName(AssetDatabase.GetAssetPath(dimensionsJson)) +
                    "\nKök obje: " + ArenaDimensionMesh.RootNameFor(
                        ResolveVenueName(AssetDatabase.GetAssetPath(dimensionsJson))),
                    MessageType.None);
            }

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "Maket taban + kolonlar + kalibrasyon işaretçileri (anchor_a kırmızı, anchor_b " +
                "mavi) üretir; duvar üretmez. Build'e GİRER çünkü işaretçileri kalibrasyon " +
                "çalışma anında kullanır — ama taban/kolon görseli oyunda çizilmez. Köşeleri " +
                "ProBuilder ile, işaretçileri sürükleyerek düzeltip 'DimensionMesh'i JSON'a " +
                "Çevir' ile aynı dosyaya geri yazabilirsin.\n\n" +
                "Sahnede ArenaBoundary varsa maket ONUN ALTINA, yerel sıfırda kurulur: arenayı " +
                "bir environment'ın üstüne oturtmak için yalnız VA_ArenaBoundary örneğini " +
                "taşırsın/döndürürsün, maket ve kalibrasyon işaretçileri onu izler. Muhafaza " +
                "yoksa sahne köküne, dünya orijininde ve dönüşsüz kurulur. Geri okuma maketin " +
                "kendi kökünü referans aldığı için taşınmış/döndürülmüş maketten de doğru çevirir.",
                MessageType.Info);

            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(dimensionsJson == null))
            {
                if (GUILayout.Button("Üret", GUILayout.Height(28f)))
                {
                    Result result = Build(dimensionsJson);
                    if (result.Success)
                    {
                        Debug.Log(
                            $"[DimensionMesh] '{result.VenueName}' maketi üretildi: " +
                            $"{result.PlanePointCount} köşeli taban + {result.ColumnCount} kolon" +
                            (result.HasCalibration ? " + A/B kalibrasyon işaretçileri" : string.Empty) +
                            $". Taban ölçüsü: {result.PlaneLocalSize.x:0.###} × " +
                            $"{result.PlaneLocalSize.y:0.###} m.",
                            result.Root);
                        Selection.activeGameObject = result.Root;
                    }
                    else
                    {
                        Debug.LogError("[DimensionMesh] Üretilemedi: " + result.Error);
                    }

                    for (int i = 0; i < result.Warnings.Count; i++)
                    {
                        Debug.LogWarning("[DimensionMesh] " + result.Warnings[i]);
                    }
                }
            }
        }

        // ---------------------------------------------------------------- üretim

        /// <summary>
        /// Boyut dosyasından maketi üretir. Exception FIRLATMAZ — hata durumunda
        /// <see cref="Result.Success"/> <c>false</c> döner.
        /// </summary>
        public static Result Build(TextAsset json)
        {
            var result = new Result();

            if (json == null)
            {
                result.Error = "Boyut dosyası verilmedi.";
                return result;
            }

            string assetPath = AssetDatabase.GetAssetPath(json);
            result.VenueName = ResolveVenueName(assetPath);

            ArenaDimensions plan = ArenaDimensions.FromTextAsset(json, out string parseError);
            if (plan == null)
            {
                result.Error = $"Boyut dosyası okunamadı ('{json.name}'): {parseError}";
                return result;
            }

            Material material = ResolveMaterial();
            if (material == null)
            {
                result.Error = "Materyal çözülemedi (URP/Lit shader yok?).";
                return result;
            }

            if (Polygon2D.IsSelfIntersecting(plan.plane))
            {
                result.Warnings.Add(
                    "Taban halkası KENDİ KENDİNİ KESİYOR — köşeler büyük ihtimalle yanlış sırada " +
                    "yazılmış. Üretilen şekil ölçülen odayı temsil etmeyebilir.");
            }

            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("VortexArena Boyut Maketi");

            // ------------------------------------------------------------- kök
            // Maket muhafazanın ALTINA, yerel sıfırda kurulur (gerekçe sınıf başlığında): arenanın
            // yerleşimini taşıyan TEK obje VA_ArenaBoundary örneği olsun, maket ve kalibrasyon
            // işaretçileri onu izlesin. Muhafaza yoksa yedek yol sahne köküdür.
            DestroyExisting(result.VenueName);

            Transform anchorParent = FindBoundaryParent();

            var root = new GameObject(ArenaDimensionMesh.RootNameFor(result.VenueName));
            Undo.RegisterCreatedObjectUndo(root, "Boyut Maketi Kökü");
            root.transform.SetParent(anchorParent, false);
            root.transform.localPosition = Vector3.zero;
            root.transform.localRotation = Quaternion.identity;
            root.transform.localScale = Vector3.one;

            if (anchorParent == null)
            {
                result.Warnings.Add(
                    "Sahnede ArenaBoundary yok — maket sahne köküne, dünya orijininde kuruldu. " +
                    "Muhafazayı 'Template Temellerini Yükle' ile kurup maketi yeniden üretirsen " +
                    "arenayı tek objeyi taşıyarak yerleştirebilirsin.");
            }

            // Maket build'e girmek ZORUNDA (kalibrasyon işaretçileri onun altında). Tag açıkça
            // sıfırlanır: eski bir sahnede 'EditorOnly' etiketli bir kök yeniden kullanılırsa
            // maket build'den sessizce düşer ve arena sahada hiç hizalanmaz.
            root.tag = "Untagged";

            var marker = Undo.AddComponent<ArenaDimensionMesh>(root);
            marker.Configure(result.VenueName, json, plan.defaultColumnHeight);
            EditorUtility.SetDirty(marker);

            result.Root = root;

            // ----------------------------------------------------------- taban
            // Taban pivotu köke eşittir: halka koordinatları planın kendi uzayında ve taban maket
            // içinde tek parça, kaydırılacak bir şey yok.
            GameObject plane = CreatePolygon(
                ArenaDimensionMesh.PlaneName,
                plan.plane,
                Vector2.zero,
                0f,
                material,
                root.transform,
                result.Warnings);

            if (plane == null)
            {
                // Yarım maket bırakılmaz: tabansız bir kök geri okumada "taban bulunamadı" diye
                // patlar ve sahnede işe yaramaz bir iskelet olarak durur.
                Undo.DestroyObjectImmediate(root);
                result.Root = null;
                result.Error = "Taban çokgeni üretilemedi (ProBuilder üçgenlemesi düştü) — " +
                               "köşe sırasını ve kendi kendini kesen kenarları gözden geçir.";
                Undo.CollapseUndoOperations(undoGroup);
                return result;
            }

            var planeMarker = plane.AddComponent<DimensionPolygon>();
            planeMarker.SetKind(DimensionPolygon.PolygonKind.Plane);
            EditorUtility.SetDirty(planeMarker);
            result.PlanePointCount = plan.plane.Length;

            Rect planeBounds = Polygon2D.Bounds(plan.plane);
            result.PlaneLocalSize = new Vector2(planeBounds.width, planeBounds.height);

            // --------------------------------------------------------- kolonlar
            ArenaDimensions.Column[] columns = plan.columns;
            if (columns != null && columns.Length > 0)
            {
                var group = new GameObject(ArenaDimensionMesh.ColumnsGroupName);
                Undo.RegisterCreatedObjectUndo(group, "Kolon Grubu");
                group.transform.SetParent(root.transform, false);

                for (int i = 0; i < columns.Length; i++)
                {
                    ArenaDimensions.Column column = columns[i];
                    string columnName = string.IsNullOrWhiteSpace(column.name) ? $"Kolon_{i:00}" : column.name;

                    if (Polygon2D.IsSelfIntersecting(column.points))
                    {
                        result.Warnings.Add($"'{columnName}' halkası kendi kendini kesiyor — köşe sırasını gözden geçir.");
                    }

                    // Pivot ağırlık merkezinde: kolonu Move tool ile sürüklemek doğal olsun diye.
                    Vector2 pivot = Polygon2D.Centroid(column.points);
                    GameObject prism = CreatePolygon(
                        columnName,
                        column.points,
                        pivot,
                        Mathf.Max(0.01f, plan.HeightOf(column)),
                        material,
                        group.transform,
                        result.Warnings);

                    if (prism == null)
                    {
                        // Taban zorunlu, kolon değil: biri düşse bile maketin geri kalanı işe yarar.
                        result.Warnings.Add($"'{columnName}' üretilemedi — atlandı.");
                        continue;
                    }

                    var prismMarker = prism.AddComponent<DimensionPolygon>();
                    prismMarker.SetKind(DimensionPolygon.PolygonKind.Column);
                    EditorUtility.SetDirty(prismMarker);
                    result.ColumnCount++;
                }
            }

            // --------------------------------------------- kalibrasyon işaretçileri
            // Zemin bandının yeri de bir ölçüdür: maketten okunup dosyaya geri yazılabilsin diye
            // geometri olarak kurulur. Nokta yazılmamış bir dosyada hiçbir şey üretilmez —
            // uydurulmuş bir çift, sahadaki bandın oraya çekildiğini söylerdi.
            if (plan.HasCalibration)
            {
                CreateMark(ArenaCalibrator.AnchorAName, DimensionAnchor.AnchorKind.A,
                    plan.calibration.a, MarkAMaterialPath, material, root.transform);
                CreateMark(ArenaCalibrator.AnchorBName, DimensionAnchor.AnchorKind.B,
                    plan.calibration.b, MarkBMaterialPath, material, root.transform);
                result.HasCalibration = true;
            }
            else
            {
                result.Warnings.Add(
                    "Dosyada kalibrasyon noktası yok ('calibration' alanı boş ya da iki nokta " +
                    $"{ArenaDimensions.MinCalibrationSpan:0.##} m'den yakın) — işaretçiler " +
                    "üretilmedi. Zemin bandının yerini bu alana yaz, yoksa arena sahada elle " +
                    "hizalanan işaretçilere kalır.");
            }

            Undo.CollapseUndoOperations(undoGroup);
            EditorSceneManager.MarkSceneDirty(root.scene);

            result.Success = true;
            return result;
        }

        /// <summary>
        /// Bir halkadan ProBuilder çokgeni üretir ve <paramref name="parent"/> altına koyar.
        /// <para>
        /// <paramref name="pivot"/> halka koordinatlarından çıkarılır ve objenin yerel konumu
        /// olarak yazılır — böylece objeyi sürüklemek çokgeni bütün olarak taşır.
        /// </para>
        /// <para>
        /// ⚠️ <paramref name="extrude"/> verildiğinde ProBuilder'ın hangi yöne uzattığı halkanın
        /// sarım yönüne bağlıdır. Sonuç bu yüzden ölçülür ve obje, <b>alt yüzü ebeveynin y=0
        /// düzleminde</b> duracak şekilde kaydırılır: geri okuma "en alttaki yatay yüz" kuralıyla
        /// çalışıyor, prizmanın havada ya da zeminin altında durması maketi okunmaz kılardı.
        /// </para>
        /// <para>
        /// ⚠️ <b>ProBuilder'ın sonucu KONTROL EDİLİR.</b> Üçgenleme düştüğünde
        /// <c>CreateShapeFromPolygon</c> exception atmaz, geriye <b>boş bir mesh</b> bırakır:
        /// sahnede adı doğru ama geometrisi olmayan bir obje kalır ve eksiklik ancak günler sonra
        /// fark edilir. Düşen çokgen bu yüzden silinir ve <c>null</c> dönülür.
        /// </para>
        /// </summary>
        /// <returns>Üretilen obje; üçgenleme düştüyse <c>null</c>.</returns>
        private static GameObject CreatePolygon(
            string name,
            Vector2[] ring,
            Vector2 pivot,
            float extrude,
            Material material,
            Transform parent,
            List<string> warnings)
        {
            var points = new List<Vector3>(ring.Length);
            for (int i = 0; i < ring.Length; i++)
            {
                points.Add(new Vector3(ring[i].x - pivot.x, 0f, ring[i].y - pivot.y));
            }

            ProBuilderMesh mesh = ProBuilderMesh.Create();
            mesh.gameObject.name = name;

            UnityEngine.ProBuilder.ActionResult shape = mesh.CreateShapeFromPolygon(points, extrude, false);
            if (!shape || mesh.faceCount == 0)
            {
                warnings.Add($"'{name}' çokgeni üretilemedi (ProBuilder: {shape.notification}).");
                Object.DestroyImmediate(mesh.gameObject);
                return null;
            }

            mesh.SetMaterial(mesh.faces, material);
            mesh.ToMesh();
            mesh.Refresh();

            Undo.RegisterCreatedObjectUndo(mesh.gameObject, "Boyut Çokgeni");
            mesh.transform.SetParent(parent, false);
            mesh.transform.localRotation = Quaternion.identity;
            mesh.transform.localScale = Vector3.one;

            float bottom = 0f;
            var filter = mesh.GetComponent<MeshFilter>();
            if (filter != null && filter.sharedMesh != null)
            {
                bottom = filter.sharedMesh.bounds.min.y;
            }

            mesh.transform.localPosition = new Vector3(pivot.x, -bottom, pivot.y);
            return mesh.gameObject;
        }

        /// <summary>
        /// Bir kalibrasyon noktası işaretçisi üretir: plan noktasında duran küçük bir küp.
        /// <para>
        /// ⚠️ Küpün MERKEZİ noktanın üstündedir (tabanı değil): geri okuma objenin transformunu
        /// aynen okuyor, yani Inspector'daki konum dosyadaki nokta ile birebir aynı görünmeli.
        /// Yarısı tabanın altında kalması bilinçlidir — nokta zemindedir.
        /// </para>
        /// <para>
        /// ⚠️ Bu sözleşme <b>çalışma anında da geçerlidir</b>: üretilen küp aynı zamanda sahnenin
        /// kalibrasyon işaretçisidir ve <c>ArenaCalibrator.PlaceMarkerAtFloor</c> onu doğrudan
        /// zemin noktasına oturtur. Tek sözleşme (transform konumu = zemin noktası) iki tarafta da
        /// aynı; ikiye ayrılırsa maketteki küp ile hizalanan işaretçi asla üst üste gelmez.
        /// </para>
        /// </summary>
        private static void CreateMark(
            string name,
            DimensionAnchor.AnchorKind kind,
            Vector2 point,
            string materialPath,
            Material fallbackMaterial,
            Transform parent)
        {
            var mark = GameObject.CreatePrimitive(PrimitiveType.Cube);
            mark.name = name;
            Undo.RegisterCreatedObjectUndo(mark, "Kalibrasyon İşaretçisi");

            // Collider maketin işi değil: free-roam'da fiziksel çarpışma zaten yok — geriye
            // yalnız ray-cast'leri (silah nişanı dahil) yakalayan bir kutu kalırdı.
            var collider = mark.GetComponent<Collider>();
            if (collider != null)
            {
                Object.DestroyImmediate(collider);
            }

            mark.transform.SetParent(parent, false);
            mark.transform.localPosition = new Vector3(point.x, 0f, point.y);
            mark.transform.localRotation = Quaternion.identity;
            mark.transform.localScale = Vector3.one * MarkSize;

            var renderer = mark.GetComponent<Renderer>();
            if (renderer != null)
            {
                var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                renderer.sharedMaterial = material != null ? material : fallbackMaterial;
            }

            var anchor = mark.AddComponent<DimensionAnchor>();
            anchor.SetKind(kind);
            EditorUtility.SetDirty(anchor);
        }

        /// <summary>
        /// Maketin bağlanacağı muhafaza transformu; sahnede <see cref="ArenaBoundary"/> yoksa
        /// <c>null</c> (maket sahne köküne kurulur).
        /// <para>
        /// ⚠️ Muhafaza her arena sahnesinde ZORUNLU ve TEKTİR; ikinci bir tanesi zaten iki farklı
        /// ölçü demektir. Yine de burada birinciye düşülür ve iş yapılır: maketi üretmeyi
        /// reddetmek, kurulumun ortasındaki bir sahneyi tümden çıkmaza sokardı.
        /// </para>
        /// </summary>
        private static Transform FindBoundaryParent()
        {
            ArenaBoundary boundary =
                Object.FindFirstObjectByType<ArenaBoundary>(FindObjectsInactive.Include);
            return boundary != null ? boundary.transform : null;
        }

        /// <summary>
        /// Aynı mekanın önceden üretilmiş maketini siler (idempotentlik).
        /// <para>
        /// Eşleşme yalnız MEKAN ADINADIR, konuma bakılmaz: maket elle taşınıp döndürülmüş olabilir
        /// ve konuma bakan bir eşleşme onu bulamayıp sahnede ikinci bir kopya bırakırdı.
        /// Başka bir mekanın maketine dokunulmaz.
        /// </para>
        /// </summary>
        private static void DestroyExisting(string venueName)
        {
            ArenaDimensionMesh[] existing =
                Object.FindObjectsByType<ArenaDimensionMesh>(FindObjectsInactive.Include, FindObjectsSortMode.None);

            for (int i = 0; i < existing.Length; i++)
            {
                ArenaDimensionMesh candidate = existing[i];
                if (candidate == null)
                {
                    continue;
                }

                if (string.Equals(candidate.VenueName, venueName, System.StringComparison.OrdinalIgnoreCase))
                {
                    Undo.DestroyObjectImmediate(candidate.gameObject);
                }
            }
        }

        // -------------------------------------------------------------- yardımcı

        /// <summary>
        /// Boyut dosyasının yolundan mekan adını türetir:
        /// <c>Assets/Arenas/Venues/&lt;Mekan&gt;/…</c> → <c>&lt;Mekan&gt;</c>.
        /// <para>
        /// ⚠️ <b>Mekan yoldan gelir, dosyanın içinden değil</b> — <c>MapDefinition</c>'daki mekan
        /// kuralının aynısı: ikinci, unutulabilir bir doğruluk kaynağı açmamak için. Dosya
        /// <c>Venues/</c> altında değilse dosya adının ilk parçasına düşülür.
        /// </para>
        /// </summary>
        public static string ResolveVenueName(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
            {
                return "Arena";
            }

            string normalized = assetPath.Replace('\\', '/');
            if (normalized.StartsWith(VenuesRoot + "/", System.StringComparison.OrdinalIgnoreCase))
            {
                string tail = normalized.Substring(VenuesRoot.Length + 1);
                int slash = tail.IndexOf('/');
                if (slash > 0)
                {
                    return tail.Substring(0, slash);
                }
            }

            string fileName = Path.GetFileNameWithoutExtension(normalized);
            int suffix = fileName.IndexOf("_dimensions", System.StringComparison.OrdinalIgnoreCase);
            return suffix > 0 ? fileName.Substring(0, suffix) : fileName;
        }

        /// <summary>
        /// Ortak mekan materyali: varsa <c>Assets/Materials/M_Mekan.mat</c>, yoksa URP/Lit ile
        /// üretilip oraya yazılır. Proje URP değilse null döner (çağıran hata bildirir).
        /// </summary>
        public static Material ResolveMaterial()
        {
            var existing = AssetDatabase.LoadAssetAtPath<Material>(SharedMaterialPath);
            if (existing != null)
            {
                return existing;
            }

            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                Debug.LogError("[DimensionMesh] URP/Lit shader bulunamadı — proje gerçekten URP mi?");
                return null;
            }

            string folder = Path.GetDirectoryName(SharedMaterialPath)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(folder) && !AssetDatabase.IsValidFolder(folder))
            {
                AssetDatabase.CreateFolder("Assets", "Materials");
            }

            var created = new Material(shader) { color = new Color(0.75f, 0.75f, 0.72f) };
            AssetDatabase.CreateAsset(created, SharedMaterialPath);
            AssetDatabase.SaveAssets();
            return created;
        }
    }
}
