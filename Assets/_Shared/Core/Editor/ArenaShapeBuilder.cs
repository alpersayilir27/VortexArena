using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.ProBuilder;
using UnityEngine.ProBuilder.MeshOperations;
using VortexArena.Core.Arena;

namespace VortexArena.Core.Editor
{
    /// <summary>
    /// Bir arena planını (<see cref="ArenaDimensions"/>) ProBuilder geometrisine çevirir:
    /// <c>Zemin</c> (çokgen yüzey) + <c>Duvarlar</c> (her kenar için bir kutu) + <c>Kolonlar</c>.
    /// <para>
    /// <b>Arena geometrisinin TEK üretim kapısıdır.</b> Ölçünün tek temsili
    /// <see cref="ArenaDimensions"/>'dır; elle yazılan boyut JSON'u da TestMesh'ten çıkarılan plan
    /// da (<see cref="ArenaTestMeshBuilder"/>) buraya girer. İkinci bir geometri üreteci ikinci bir
    /// doğruluk kaynağı olurdu — bu yüzden "alan dikdörtgense şu kısa yol" gibi bir ayrım YOKTUR:
    /// alan tam kare bile olsa dört köşeli bir <c>outline</c>'dır.
    /// </para>
    /// <para>
    /// Menü girişi: <c>Tools &gt; VortexArena &gt; Build Arena From Dimensions</c> (seçimde boyut
    /// JSON'u = <c>TextAsset</c>). Sihirbaz (<see cref="ArenaTemplateWizard"/>) de aynı kapıdan
    /// geçer.
    /// </para>
    /// <para>
    /// ⚠️ <b>Kök = <see cref="ArenaBoundary"/>'yi taşıyan transform.</b> Plan koordinatları o
    /// transformun yerel XZ düzlemindedir (asset'in kendi belgesine bakın); geometriyi başka bir
    /// objenin altına üretmek planı sessizce kaydırır. Sihirbaz kökü sahnedeki
    /// <c>ArenaBoundary</c>'den bulur, elle çalıştırırken de seçili obje o olmalıdır.
    /// </para>
    /// <para>
    /// ⚠️ <b>Üretilen her şey tek bir dalda toplanır:</b> <c>Zemin</c>/<c>Duvarlar</c>/
    /// <c>Kolonlar</c> doğrudan köke değil, kökün <see cref="GeometryRootName"/> adlı çocuğunun
    /// altına kurulur. Sebep: arena geometrisi kalibrasyon işaretçileri, taban bölgeleri ve rig
    /// ile aynı seviyede karışmasın; tek tıkla gizlenip gösterilebilsin.
    /// </para>
    /// <para>
    /// ⚠️ <b>Idempotent:</b> aynı kök üstünde tekrar çalıştırmak eski geometriyi siler. Plan
    /// değişince aracı yeniden çalıştırmak yeterlidir, sahnede ikinci bir kopya birikmez.
    /// Şablondan gelen ÖTEKİ objelere (kalibrasyon işaretçileri, taban bölgeleri, rig) dokunmaz.
    /// </para>
    /// <para>
    /// ⚠️ Sonda <c>EditorUtility.DisplayDialog</c> YOK: modal dialog Unity ana thread'ini
    /// kilitliyor ve CLI ile çalıştırıldığında komut timeout veriyor (bkz.
    /// <c>ServerConfigExporter</c> tuzağı). Sonuç <c>Debug.Log</c> ile bildirilir.
    /// </para>
    /// </summary>
    internal static class ArenaShapeBuilder
    {
        /// <summary>
        /// Üretilen geometrinin tek ortak ebeveyni — kökün altındaki çocuk objenin adı.
        /// Idempotent temizlik ve göç bu adı arar.
        /// </summary>
        public const string GeometryRootName = "ArenaGeometry";

        /// <summary>Zemin objesinin adı — idempotent temizlik bu adı arar.</summary>
        public const string FloorName = "Zemin";

        /// <summary>Duvar grubunun adı.</summary>
        public const string WallsGroupName = "Duvarlar";

        /// <summary>Kolon grubunun adı.</summary>
        public const string ColumnsGroupName = "Kolonlar";

        /// <summary>
        /// Duvar kutusunun kalınlığı (metre). Duvar kenar çizgisinin ÜSTÜNE ortalanır — plan
        /// ölçüsü oyuncunun yürüyebileceği alandır, kalınlık dışa değil iki yana yarım yarım
        /// taşar ki muhafaza mesafesi ile görsel duvar bir arada kalsın.
        /// </summary>
        private const float WallThickness = 0.15f;

        private const string SharedMaterialPath = "Assets/Materials/M_Mekan.mat";

        /// <summary>Üretim sonucu — çağıran duvarları <c>ArenaBoundary.wallRenderers</c>'a bağlar.</summary>
        internal sealed class Result
        {
            /// <summary>Üretilen zemin objesi (plan geçersizse null).</summary>
            public GameObject Floor;

            /// <summary>
            /// Üretilen her şeyin toplandığı ortak ebeveyn (<see cref="GeometryRootName"/>).
            /// Başarısız üretimde null.
            /// </summary>
            public GameObject GeometryRoot;

            /// <summary>Üretilen duvarların Renderer'ları, kenar sırasıyla.</summary>
            public readonly List<MeshRenderer> WallRenderers = new List<MeshRenderer>();

            /// <summary>Üretilen kolon objeleri.</summary>
            public readonly List<GameObject> Columns = new List<GameObject>();

            /// <summary>
            /// Üretilen geometrinin arena YEREL XZ sınırlayıcı kutusu — yalnız RAPORLAMA içindir
            /// (log satırı / sihirbaz özeti).
            /// <para>
            /// ⚠️ Buradan hiçbir bileşen alanı doldurulmaz: muhafaza ölçüsü de admin kuş bakışı
            /// kadrajı da <see cref="ArenaBoundary"/>'nin okuduğu boyut JSON'undan gelir. Ölçüyü
            /// ikinci bir yere yazmak tam olarak kaçındığımız şey.
            /// </para>
            /// </summary>
            public Rect LocalBounds;

            /// <summary>Üretim gerçekleşti mi.</summary>
            public bool Success;

            /// <summary>Başarısızsa sebebi (kullanıcıya gösterilecek metin).</summary>
            public string Error;
        }

        // ------------------------------------------------------------- üretim

        /// <summary>
        /// Planı <paramref name="root"/> altında geometriye çevirir. Exception FIRLATMAZ —
        /// hata durumunda <see cref="Result.Success"/> <c>false</c> döner (sihirbaz kısmi bir
        /// arena kutusuyla yarıda kalmasın diye).
        /// </summary>
        /// <param name="plan">Zemin sınırı + kolonlar.</param>
        /// <param name="root">Arena kökü — <see cref="ArenaBoundary"/>'yi taşıyan transform.</param>
        /// <param name="material">Zemin/duvar/kolon materyali; null ise ortak mekan materyali.</param>
        public static Result Build(ArenaDimensions plan, Transform root, Material material = null)
        {
            var result = new Result();

            if (plan == null || !plan.IsValid)
            {
                result.Error = $"Plan boş ya da geçersiz (en az {ArenaDimensions.MinOutlinePoints} köşe gerekir).";
                return result;
            }

            if (root == null)
            {
                result.Error = "Arena kökü verilmedi.";
                return result;
            }

            Material mat = material != null ? material : ResolveMaterial();
            if (mat == null)
            {
                result.Error = "Materyal çözülemedi (URP Lit shader yok?).";
                return result;
            }

            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("VortexArena Arena Şekli");

            ClearGenerated(root);
            Transform geometryRoot = CreateGeometryRoot(root);
            result.GeometryRoot = geometryRoot.gameObject;

            Vector2[] outline = plan.outline;

            // ------------------------------------------------------------- zemin
            // CreateShapeFromPolygon extrude=0 → düz yüzey; duvarlar ayrı üretiliyor çünkü
            // ArenaBoundary alfalarını renderer BAŞINA kısıyor (tek extrude mesh olsaydı
            // zemin de duvarla birlikte solardı).
            var floorPoints = new List<Vector3>(outline.Length);
            for (int i = 0; i < outline.Length; i++)
            {
                floorPoints.Add(new Vector3(outline[i].x, 0f, outline[i].y));
            }

            ProBuilderMesh floor = ProBuilderMesh.Create();
            floor.gameObject.name = FloorName;
            floor.CreateShapeFromPolygon(floorPoints, 0f, false);
            Finalize(floor, mat, geometryRoot);
            result.Floor = floor.gameObject;

            // ----------------------------------------------------------- duvarlar
            var wallsGroup = new GameObject(WallsGroupName);
            Undo.RegisterCreatedObjectUndo(wallsGroup, "Arena Duvarları");
            wallsGroup.transform.SetParent(geometryRoot, false);

            float height = Mathf.Max(0.01f, plan.wallHeight);
            for (int i = 0; i < outline.Length; i++)
            {
                // Kapalı çokgen: son köşe ilk köşeye bağlanır (asset tekrarı yasak).
                Vector2 a = outline[i];
                Vector2 b = outline[(i + 1) % outline.Length];
                Vector2 delta = b - a;
                float length = delta.magnitude;
                if (length <= Mathf.Epsilon)
                {
                    continue; // üst üste binen köşe: duvar üretmeye değmez
                }

                Vector2 dir = delta / length;
                ProBuilderMesh wall = ShapeGenerator.GenerateCube(
                    PivotLocation.Center,
                    new Vector3(length, height, WallThickness));

                wall.gameObject.name = $"Duvar_{i:00}";
                Finalize(wall, mat, wallsGroup.transform);

                Vector2 mid = (a + b) * 0.5f;
                wall.transform.localPosition = new Vector3(mid.x, height * 0.5f, mid.y);
                // Kutunun +X'i kenar boyunca olsun: ileri = kenarın XZ normali.
                wall.transform.localRotation = Quaternion.LookRotation(
                    new Vector3(dir.y, 0f, -dir.x), Vector3.up);

                var renderer = wall.GetComponent<MeshRenderer>();
                if (renderer != null)
                {
                    result.WallRenderers.Add(renderer);
                }
            }

            // ----------------------------------------------------------- kolonlar
            ArenaDimensions.Column[] columns = plan.columns;
            if (columns != null && columns.Length > 0)
            {
                var columnsGroup = new GameObject(ColumnsGroupName);
                Undo.RegisterCreatedObjectUndo(columnsGroup, "Arena Kolonları");
                columnsGroup.transform.SetParent(geometryRoot, false);

                for (int i = 0; i < columns.Length; i++)
                {
                    ArenaDimensions.Column column = columns[i];
                    float columnHeight = Mathf.Max(0.01f, plan.HeightOf(column));

                    ProBuilderMesh box = ShapeGenerator.GenerateCube(
                        PivotLocation.Center,
                        new Vector3(column.size.x, columnHeight, column.size.y));

                    box.gameObject.name = string.IsNullOrWhiteSpace(column.name)
                        ? $"Kolon_{i}"
                        : column.name;
                    Finalize(box, mat, columnsGroup.transform);

                    // Kutu merkezden pivotlu: zemine oturması için yarım yükseklik yukarı.
                    box.transform.localPosition = new Vector3(column.center.x, columnHeight * 0.5f, column.center.y);
                    box.transform.localRotation = Quaternion.Euler(0f, column.yaw, 0f);

                    ApplyObstacle(box.gameObject, column.size);
                    result.Columns.Add(box.gameObject);
                }
            }

            Undo.CollapseUndoOperations(undoGroup);

            result.LocalBounds = plan.LocalBounds();
            result.Success = true;
            return result;
        }

        // -------------------------------------------------------- ArenaBoundary

        /// <summary>
        /// Üretim sonrası <see cref="ArenaBoundary"/>'yi bağlar: boyut dosyası + duvar Renderer'ları.
        /// <para>
        /// ⚠️ <b>Boyut dosyası her zaman bağlanır</b> — her iki üretim yolunda da (elle yazılan JSON
        /// ve TestMesh'ten çıkarılan JSON) ortada bir <c>TextAsset</c> vardır. Bağlanmazsa muhafaza
        /// ölçüsüz kalır ve <c>ArenaBoundary</c> devre dışı düşer; arena sessizce sınırsız olurdu.
        /// </para>
        /// <para>
        /// Alanlar <c>[SerializeField] private</c> olduğu için <see cref="SerializedObject"/>
        /// üzerinden yazılır; alan bulunamazsa kod kırılmaz, <paramref name="warnings"/>'e elle
        /// bağlama notu düşer. <c>head</c>/<c>fadeRenderer</c>/<c>warningText</c>'e DOKUNULMAZ —
        /// onlar rig prefabına bakar ve şablondan doğru gelir.
        /// </para>
        /// </summary>
        internal static void BindBoundary(
            ArenaBoundary boundary,
            TextAsset dimensionsAsset,
            IList<MeshRenderer> walls,
            List<string> warnings)
        {
            if (boundary == null)
            {
                return;
            }

            var serialized = new SerializedObject(boundary);

            SerializedProperty jsonProp = serialized.FindProperty("dimensionsJson");
            if (jsonProp != null)
            {
                jsonProp.objectReferenceValue = dimensionsAsset;
            }
            else
            {
                warnings?.Add(
                    "ArenaBoundary'de 'dimensionsJson' alanı bulunamadı — boyut dosyasını alana ELLE bağla, " +
                    "yoksa muhafaza ölçüsüz kalır.");
            }

            SerializedProperty wallsProp = serialized.FindProperty("wallRenderers");
            if (wallsProp != null && wallsProp.isArray)
            {
                int count = walls?.Count ?? 0;
                wallsProp.arraySize = count;
                for (int i = 0; i < count; i++)
                {
                    wallsProp.GetArrayElementAtIndex(i).objectReferenceValue = walls[i];
                }
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(boundary);
        }

        /// <summary>
        /// Üretilen geometrinin ortak ebeveynini açar (yerel dönüşümü sıfırlanmış olarak) —
        /// plan koordinatları kökün yerel XZ'sinde olduğu için ara ebeveyn kaydırma/döndürme
        /// TAŞIMAMALIDIR.
        /// </summary>
        internal static Transform CreateGeometryRoot(Transform root)
        {
            var geometryRoot = new GameObject(GeometryRootName);
            Undo.RegisterCreatedObjectUndo(geometryRoot, "Arena Geometri Kökü");
            geometryRoot.transform.SetParent(root, false);
            geometryRoot.transform.localPosition = Vector3.zero;
            geometryRoot.transform.localRotation = Quaternion.identity;
            geometryRoot.transform.localScale = Vector3.one;
            return geometryRoot.transform;
        }

        /// <summary>
        /// Daha önce üretilmiş geometriyi siler: kökün <see cref="GeometryRootName"/> çocukları
        /// <b>ve</b> eski düzenden kalmış doğrudan <c>Zemin</c>/<c>Duvarlar</c>/<c>Kolonlar</c>
        /// çocukları.
        /// <para>
        /// ⚠️ İkinci grup bir <b>göç adımıdır</b>: mevcut sahneler (ör.
        /// <c>Venues/VortexAntep/Default/Scenes/ArenaVortexAntep.unity</c>) geometriyi ortak
        /// ebeveyn YOKKEN üretilmiş. Silinmezse aracı yeniden çalıştırmak eski geometriyi
        /// sahnede bırakır ve arena iki kat çizilirdi.
        /// </para>
        /// <para>
        /// Aynı adla birden çok çocuk olabileceği için (elle kopyalanmış olabilir) hepsi taranır.
        /// </para>
        /// </summary>
        internal static void ClearGenerated(Transform root)
        {
            for (int i = root.childCount - 1; i >= 0; i--)
            {
                Transform child = root.GetChild(i);
                if (child.name == GeometryRootName ||
                    child.name == FloorName ||
                    child.name == WallsGroupName ||
                    child.name == ColumnsGroupName)
                {
                    Undo.DestroyObjectImmediate(child.gameObject);
                }
            }
        }

        /// <summary>
        /// Kolona <c>ArenaObstacle</c> ekler ve ölçüsünü yazar.
        /// <para>
        /// Alan <c>[SerializeField] private</c> olabileceği için <see cref="SerializedObject"/>
        /// üzerinden doldurulur — erişim değişse de bu kod kırılmaz.
        /// </para>
        /// </summary>
        internal static void ApplyObstacle(GameObject target, Vector2 size)
        {
            ArenaObstacle obstacle = target.GetComponent<ArenaObstacle>();
            if (obstacle == null)
            {
                obstacle = Undo.AddComponent<ArenaObstacle>(target);
            }

            var serialized = new SerializedObject(obstacle);
            SerializedProperty sizeProp = serialized.FindProperty("size");
            if (sizeProp != null)
            {
                sizeProp.vector2Value = size;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }

            EditorUtility.SetDirty(obstacle);
        }

        /// <summary>ProBuilder mesh'ini materyalle kapatıp köke bağlar (yerel dönüşüm sıfırlanır).</summary>
        internal static void Finalize(ProBuilderMesh mesh, Material material, Transform parent)
        {
            mesh.SetMaterial(mesh.faces, material);
            mesh.ToMesh();
            mesh.Refresh();
            Undo.RegisterCreatedObjectUndo(mesh.gameObject, "Arena Geometrisi");
            mesh.transform.SetParent(parent, false);
            mesh.transform.localPosition = Vector3.zero;
            mesh.transform.localRotation = Quaternion.identity;
        }

        /// <summary>
        /// Ortak mekan materyali: varsa <c>Assets/Materials/M_Mekan.mat</c>, yoksa URP/Lit ile
        /// üretilip oraya yazılır. Proje URP değilse null döner (çağıran hata bildirir).
        /// </summary>
        internal static Material ResolveMaterial()
        {
            var existing = AssetDatabase.LoadAssetAtPath<Material>(SharedMaterialPath);
            if (existing != null)
            {
                return existing;
            }

            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                Debug.LogError("[ArenaShape] URP/Lit shader bulunamadı — proje gerçekten URP mi?");
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

        // --------------------------------------------------------------- menü

        /// <summary>
        /// <c>Tools &gt; VortexArena &gt; Build Arena From Dimensions</c> — seçili boyut JSON'unu
        /// (<c>TextAsset</c>) aktif sahnedeki arena kökü üstünde geometriye çevirir.
        /// <para>
        /// Ölçüyü dosyada elle düzeltip aynı menüden yeniden çizmek içindir: üretim idempotenttir,
        /// eski geometri silinip yenisi konur.
        /// </para>
        /// <para>
        /// Kök seçimi: seçimde bir SAHNE objesi varsa o; yoksa sahnedeki
        /// <see cref="ArenaBoundary"/>'nin transformu. İkisi de yoksa iş yapılmaz — plan yanlış
        /// transformun altına düşerse sessizce kaymış bir arena üretilirdi.
        /// </para>
        /// </summary>
        [MenuItem("Tools/VortexArena/Build Arena From Dimensions")]
        private static void BuildFromSelection()
        {
            TextAsset json = FindSelectedTextAsset();
            if (json == null)
            {
                Debug.LogError("[ArenaShape] Project penceresinden bir boyut dosyası (JSON / TextAsset) seç.");
                return;
            }

            ArenaDimensions plan = ArenaDimensions.FromTextAsset(json, out string planError);
            if (plan == null)
            {
                Debug.LogError($"[ArenaShape] Boyut dosyası okunamadı ('{json.name}'): {planError}");
                return;
            }

            Transform root = FindSelectedRoot();
            if (root == null)
            {
                Debug.LogError(
                    "[ArenaShape] Arena kökü bulunamadı: sahnede ArenaBoundary taşıyan bir obje yok. " +
                    "Kökü hiyerarşiden seçerek tekrar dene.");
                return;
            }

            Result result = Build(plan, root);
            if (!result.Success)
            {
                Debug.LogError($"[ArenaShape] Üretilemedi: {result.Error}");
                return;
            }

            // Muhafazayı da burada bağlıyoruz: elle "wallRenderers'ı doldurmayı unutma" demek,
            // unutulduğunda sessizce solmayan duvarlar üretiyordu.
            ArenaBoundary boundary = root.GetComponent<ArenaBoundary>();
            var warnings = new List<string>();
            BindBoundary(boundary, json, result.WallRenderers, warnings);

            Debug.Log(
                $"[ArenaShape] '{json.name}' → '{root.name}/{GeometryRootName}': zemin + " +
                $"{result.WallRenderers.Count} duvar + {result.Columns.Count} kolon üretildi " +
                $"(yerel sınır {result.LocalBounds.width:0.##}×{result.LocalBounds.height:0.##} m).");

            if (boundary == null)
            {
                Debug.LogWarning(
                    "[ArenaShape] Kökte ArenaBoundary yok — boyut dosyasını ve duvarları ELLE bağla.");
            }

            for (int i = 0; i < warnings.Count; i++)
            {
                Debug.LogWarning("[ArenaShape] " + warnings[i]);
            }

            Selection.activeGameObject = root.gameObject;
        }

        [MenuItem("Tools/VortexArena/Build Arena From Dimensions", true)]
        private static bool ValidateBuildFromSelection()
        {
            // Ayrıştırma denenmez, yalnız TÜR bakılır: menü doğrulaması her repaint'te koşuyor,
            // JSON ayrıştırmak orada gereksiz maliyet olurdu (bozuk dosya çalıştırınca raporlanır).
            return FindSelectedTextAsset() != null;
        }

        /// <summary>Seçimdeki ilk <c>TextAsset</c> (boyut JSON'u adayı).</summary>
        private static TextAsset FindSelectedTextAsset()
        {
            Object[] selection = Selection.objects;
            for (int i = 0; i < selection.Length; i++)
            {
                if (selection[i] is TextAsset text)
                {
                    return text;
                }
            }

            return null;
        }

        /// <summary>Seçili sahne objesi, yoksa sahnedeki <see cref="ArenaBoundary"/>'nin transformu.</summary>
        internal static Transform FindSelectedRoot()
        {
            GameObject[] selection = Selection.gameObjects;
            for (int i = 0; i < selection.Length; i++)
            {
                if (selection[i] != null && selection[i].scene.IsValid())
                {
                    return selection[i].transform;
                }
            }

            var boundary = Object.FindFirstObjectByType<ArenaBoundary>(FindObjectsInactive.Include);
            return boundary != null ? boundary.transform : null;
        }
    }
}
