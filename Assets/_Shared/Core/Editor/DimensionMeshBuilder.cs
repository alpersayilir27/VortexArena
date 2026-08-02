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
    /// <c>Tools &gt; VortexArena &gt; JSON'dan DimensionMesh Üret</c> — bir mekanın boyut
    /// dosyasını (<see cref="ArenaDimensions"/>) sahnedeki <b>ölçü maketine</b> çevirir:
    /// <c>&lt;Mekan&gt;_DimensionMesh</c> kökü altında tek bir <c>Plane</c> çokgeni ve her kolon
    /// için bir prizma.
    /// <para>
    /// <b>Maket oynanan geometri DEĞİLDİR</b> — kök <c>EditorOnly</c> etiketlidir, build'e
    /// girmez. Arena sanatı bunun üstüne kurulur; duvar ÜRETİLMEZ (arenanın duvarları
    /// environment'a aittir).
    /// </para>
    /// <para>
    /// ⚠️ <b>Kök, sahnedeki <see cref="ArenaBoundary"/>'nin altına kurulur</b> (yerel dönüşümü
    /// sıfırlanmış olarak): boyut dosyasındaki koordinatlar muhafaza transformunun yerel
    /// XZ'sindedir. Sahnede muhafaza yoksa maket sahne köküne düşer ve uyarı basılır — koordinatlar
    /// o durumda muhafaza uzayıyla hizalı DEĞİLDİR. Bu yüzden akışta önce
    /// <c>Template Temellerini Yükle</c> çalıştırılır.
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

            /// <summary>Üretim gerçekleşti mi.</summary>
            public bool Success;

            /// <summary>Başarısızsa sebebi.</summary>
            public string Error;

            /// <summary>Kurtarılmış ama dikkat isteyen durumlar.</summary>
            public readonly List<string> Warnings = new List<string>();
        }

        // --------------------------------------------------------------- pencere

        [MenuItem("Tools/VortexArena/JSON'dan DimensionMesh Üret")]
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
                "Maket yalnız ÖLÇÜ REFERANSIDIR: taban + kolonlar üretilir, duvar üretilmez ve kök " +
                "'EditorOnly' etiketlendiği için build'e girmez. Köşeleri ProBuilder ile düzeltip " +
                "'DimensionMesh'i JSON'a Çevir' ile aynı dosyaya geri yazabilirsin.",
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
                            $"{result.PlanePointCount} köşeli taban + {result.ColumnCount} kolon.",
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
            var boundary = Object.FindFirstObjectByType<ArenaBoundary>(FindObjectsInactive.Include);
            Transform parent = boundary != null ? boundary.transform : null;
            if (parent == null)
            {
                result.Warnings.Add(
                    "Sahnede ArenaBoundary YOK — maket sahne köküne kuruldu ve koordinatları " +
                    "muhafaza uzayıyla hizalı DEĞİL. Önce 'Template Temellerini Yükle' çalıştırıp " +
                    "maketi yeniden üret.");
            }

            DestroyExisting(result.VenueName, parent);

            var root = new GameObject(ArenaDimensionMesh.RootNameFor(result.VenueName));
            Undo.RegisterCreatedObjectUndo(root, "Boyut Maketi Kökü");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = Vector3.zero;
            root.transform.localRotation = Quaternion.identity;
            root.transform.localScale = Vector3.one;
            root.tag = ArenaDimensionMesh.EditorOnlyTag;

            var marker = Undo.AddComponent<ArenaDimensionMesh>(root);
            marker.Configure(result.VenueName, json, plan.defaultColumnHeight);
            EditorUtility.SetDirty(marker);

            result.Root = root;

            // ----------------------------------------------------------- taban
            // Taban pivotu köke eşittir: halka koordinatları zaten muhafaza yerel XZ'sinde ve
            // taban maket içinde tek parça, kaydırılacak bir şey yok.
            GameObject plane = CreatePolygon(
                ArenaDimensionMesh.PlaneName,
                plan.plane,
                Vector2.zero,
                0f,
                material,
                root.transform);

            var planeMarker = plane.AddComponent<DimensionPolygon>();
            planeMarker.SetKind(DimensionPolygon.PolygonKind.Plane);
            EditorUtility.SetDirty(planeMarker);
            result.PlanePointCount = plan.plane.Length;

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
                        group.transform);

                    var prismMarker = prism.AddComponent<DimensionPolygon>();
                    prismMarker.SetKind(DimensionPolygon.PolygonKind.Column);
                    EditorUtility.SetDirty(prismMarker);
                    result.ColumnCount++;
                }
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
        /// </summary>
        private static GameObject CreatePolygon(
            string name,
            Vector2[] ring,
            Vector2 pivot,
            float extrude,
            Material material,
            Transform parent)
        {
            var points = new List<Vector3>(ring.Length);
            for (int i = 0; i < ring.Length; i++)
            {
                points.Add(new Vector3(ring[i].x - pivot.x, 0f, ring[i].y - pivot.y));
            }

            ProBuilderMesh mesh = ProBuilderMesh.Create();
            mesh.gameObject.name = name;
            mesh.CreateShapeFromPolygon(points, extrude, false);
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
        /// Aynı mekanın önceden üretilmiş maketini siler (idempotentlik).
        /// <para>
        /// Önce beklenen ebeveynin altına, sonra tüm sahneye bakılır: maket elle başka bir yere
        /// taşınmış olabilir ve iki kopya bırakmak sessizce çift geometri üretirdi.
        /// </para>
        /// </summary>
        private static void DestroyExisting(string venueName, Transform preferredParent)
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

                bool sameVenue = string.Equals(candidate.VenueName, venueName, System.StringComparison.OrdinalIgnoreCase);
                bool sameParent = preferredParent != null && candidate.transform.parent == preferredParent;
                if (sameVenue || sameParent)
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
