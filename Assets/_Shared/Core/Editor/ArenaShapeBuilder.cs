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
    /// Bir <see cref="ArenaShapeDefinition"/> planını ProBuilder geometrisine çevirir:
    /// <c>Zemin</c> (çokgen yüzey) + <c>Duvarlar</c> (her kenar için bir kutu) + <c>Kolonlar</c>.
    /// <para>
    /// Menü girişi: <c>Tools &gt; VortexArena &gt; Build Arena From Shape</c>. Sihirbaz
    /// (<see cref="ArenaTemplateWizard"/>) de aynı kapıdan geçer — plan doluysa şablondan gelen
    /// hazır zemin/duvarı bununla değiştirir.
    /// </para>
    /// <para>
    /// ⚠️ <b>Kök = <see cref="ArenaBoundary"/>'yi taşıyan transform.</b> Plan koordinatları o
    /// transformun yerel XZ düzlemindedir (asset'in kendi belgesine bakın); geometriyi başka bir
    /// objenin altına üretmek planı sessizce kaydırır. Sihirbaz kökü sahnedeki
    /// <c>ArenaBoundary</c>'den bulur, elle çalıştırırken de seçili obje o olmalıdır.
    /// </para>
    /// <para>
    /// ⚠️ <b>Idempotent:</b> aynı kök üstünde tekrar çalıştırmak eski <c>Zemin</c>/<c>Duvarlar</c>/
    /// <c>Kolonlar</c> çocuklarını siler. Plan değişince aracı yeniden çalıştırmak yeterlidir,
    /// sahnede ikinci bir kopya birikmez. Şablondan gelen ÖTEKİ objelere (kalibrasyon işaretçileri,
    /// taban bölgeleri, rig) dokunmaz — onlar bu üç addan farklıdır.
    /// </para>
    /// <para>
    /// ⚠️ Sonda <c>EditorUtility.DisplayDialog</c> YOK: modal dialog Unity ana thread'ini
    /// kilitliyor ve CLI ile çalıştırıldığında komut timeout veriyor (bkz.
    /// <c>ServerConfigExporter</c> tuzağı). Sonuç <c>Debug.Log</c> ile bildirilir.
    /// </para>
    /// </summary>
    internal static class ArenaShapeBuilder
    {
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

            /// <summary>Üretilen duvarların Renderer'ları, kenar sırasıyla.</summary>
            public readonly List<MeshRenderer> WallRenderers = new List<MeshRenderer>();

            /// <summary>Üretilen kolon objeleri.</summary>
            public readonly List<GameObject> Columns = new List<GameObject>();

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
        /// <param name="shape">Zemin sınırı + kolonlar.</param>
        /// <param name="root">Arena kökü — <see cref="ArenaBoundary"/>'yi taşıyan transform.</param>
        /// <param name="material">Zemin/duvar/kolon materyali; null ise ortak mekan materyali.</param>
        public static Result Build(ArenaShapeDefinition shape, Transform root, Material material = null)
        {
            var result = new Result();

            if (shape == null || !shape.IsValid)
            {
                result.Error = $"Plan boş ya da geçersiz (en az {ArenaShapeDefinition.MinOutlinePoints} köşe gerekir).";
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

            Vector2[] outline = shape.Outline;

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
            Finalize(floor, mat, root);
            result.Floor = floor.gameObject;

            // ----------------------------------------------------------- duvarlar
            var wallsGroup = new GameObject(WallsGroupName);
            Undo.RegisterCreatedObjectUndo(wallsGroup, "Arena Duvarları");
            wallsGroup.transform.SetParent(root, false);

            float height = Mathf.Max(0.01f, shape.WallHeight);
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
            ArenaShapeDefinition.Column[] columns = shape.Columns;
            if (columns != null && columns.Length > 0)
            {
                var columnsGroup = new GameObject(ColumnsGroupName);
                Undo.RegisterCreatedObjectUndo(columnsGroup, "Arena Kolonları");
                columnsGroup.transform.SetParent(root, false);

                for (int i = 0; i < columns.Length; i++)
                {
                    ArenaShapeDefinition.Column column = columns[i];
                    float columnHeight = Mathf.Max(0.01f, shape.HeightOf(column));

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

            result.Success = true;
            return result;
        }

        /// <summary>
        /// Daha önce üretilmiş <c>Zemin</c>/<c>Duvarlar</c>/<c>Kolonlar</c> çocuklarını siler.
        /// Aynı adla birden çok çocuk olabileceği için (elle kopyalanmış olabilir) hepsi taranır.
        /// </summary>
        private static void ClearGenerated(Transform root)
        {
            for (int i = root.childCount - 1; i >= 0; i--)
            {
                Transform child = root.GetChild(i);
                if (child.name == FloorName || child.name == WallsGroupName || child.name == ColumnsGroupName)
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
        private static void ApplyObstacle(GameObject target, Vector2 size)
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
        private static void Finalize(ProBuilderMesh mesh, Material material, Transform parent)
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
        private static Material ResolveMaterial()
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
        /// <c>Tools &gt; VortexArena &gt; Build Arena From Shape</c> — seçili
        /// <see cref="ArenaShapeDefinition"/> asset'ini aktif sahnedeki arena kökü üstünde üretir.
        /// <para>
        /// Kök seçimi: seçimde bir SAHNE objesi varsa o; yoksa sahnedeki
        /// <see cref="ArenaBoundary"/>'nin transformu. İkisi de yoksa iş yapılmaz — plan yanlış
        /// transformun altına düşerse sessizce kaymış bir arena üretilirdi.
        /// </para>
        /// </summary>
        [MenuItem("Tools/VortexArena/Build Arena From Shape")]
        private static void BuildFromSelection()
        {
            ArenaShapeDefinition shape = FindSelectedShape();
            if (shape == null)
            {
                Debug.LogError("[ArenaShape] Project penceresinden bir ArenaShapeDefinition asset'i seç.");
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

            Result result = Build(shape, root);
            if (!result.Success)
            {
                Debug.LogError($"[ArenaShape] Üretilemedi: {result.Error}");
                return;
            }

            Debug.Log(
                $"[ArenaShape] '{shape.name}' → '{root.name}': zemin + {result.WallRenderers.Count} duvar + " +
                $"{result.Columns.Count} kolon üretildi. " +
                "Duvarları ArenaBoundary.wallRenderers alanına bağlamayı unutma.");
            Selection.activeGameObject = root.gameObject;
        }

        [MenuItem("Tools/VortexArena/Build Arena From Shape", true)]
        private static bool ValidateBuildFromSelection()
        {
            return FindSelectedShape() != null;
        }

        /// <summary>Seçimdeki ilk <see cref="ArenaShapeDefinition"/> asset'i.</summary>
        private static ArenaShapeDefinition FindSelectedShape()
        {
            Object[] selection = Selection.objects;
            for (int i = 0; i < selection.Length; i++)
            {
                if (selection[i] is ArenaShapeDefinition shape)
                {
                    return shape;
                }
            }

            return null;
        }

        /// <summary>Seçili sahne objesi, yoksa sahnedeki <see cref="ArenaBoundary"/>'nin transformu.</summary>
        private static Transform FindSelectedRoot()
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
