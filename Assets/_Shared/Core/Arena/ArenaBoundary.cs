using System.Collections.Generic;
using UnityEngine;

namespace VortexArena.Core.Arena
{
    /// <summary>
    /// Free-roam arena guard for a physical play space. The player moves
    /// 1:1 with their real body; this component watches the HMD position in the
    /// arena's local space and (a) fades in the boundary walls as the player
    /// approaches the edge, (b) fades the screen to black and shows a warning
    /// when they step outside the allowed area.
    /// Attach to an object positioned at the arena center, aligned with the
    /// arena's rotation; walls are expected to sit at +/- halfExtent.
    /// <para>
    /// <b>İki hesap kipi vardır:</b> <see cref="shape"/> BOŞSA eksene hizalı dikdörtgen (hızlı
    /// yol, mevcut arenaların tamamı böyle çalışır); DOLUYSA plan çokgeni + engeller
    /// (<see cref="ArenaShapeDefinition.Columns"/> ve sahnedeki <see cref="ArenaObstacle"/>'lar).
    /// Uyarı/karartma mantığı iki kipte de aynıdır — yalnız "kenara uzaklık" değerinin kaynağı
    /// değişir.
    /// </para>
    /// <para>
    /// ⚠️ Bu bileşen <b>arena uzayının origin'i DEĞİLDİR</b>: ağ koordinatlarının sıfırı
    /// <see cref="SpawnPoint"/>'tedir (<see cref="ArenaSpace"/>). Muhafaza objesini büyütmek ya da
    /// kaydırmak oyuncuların ağ konumunu etkilemez.
    /// </para>
    /// </summary>
    public class ArenaBoundary : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("HMD transform (CenterEyeAnchor). Falls back to Camera.main.")]
        [SerializeField] private Transform head;
        [SerializeField] private Renderer[] wallRenderers;
        [Tooltip("Quad parented to the HMD used for the out-of-bounds blackout.")]
        [SerializeField] private Renderer fadeRenderer;
        [SerializeField] private TextMesh warningText;

        [Header("Arena size (meters)")]
        [SerializeField] private float halfExtentX = 5f;
        [SerializeField] private float halfExtentZ = 5f;

        [Tooltip("İsteğe bağlı arena planı (yamuk/L şeklindeki alanlar + kolonlar). BOŞ bırakılırsa " +
                 "yukarıdaki dikdörtgen ölçü kullanılır.")]
        [SerializeField] private ArenaShapeDefinition shape;

        [Header("Warning behaviour")]
        [Tooltip("Distance from the edge (m) where the walls start brightening.")]
        [SerializeField] private float warnDistance = 1f;
        [SerializeField] private float minWallAlpha = 0.05f;
        [SerializeField] private float maxWallAlpha = 0.85f;
        [Tooltip("Meters past the boundary at which the blackout is fully opaque.")]
        [SerializeField] private float fadeOutsideDistance = 0.3f;
        [SerializeField] private float maxFadeAlpha = 0.96f;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private MaterialPropertyBlock propertyBlock;

        /// <summary>True while the HMD is outside the allowed area.</summary>
        public bool IsOutOfBounds { get; private set; }

        /// <summary>
        /// Arena yarı ölçüleri (metre, X/Z) — admin kuş bakışı kadrajı bunu okur. Plan varsa
        /// çokgenin sınırlayıcı kutusundan, yoksa dikdörtgen alanlardan gelir.
        /// </summary>
        public Vector2 HalfExtents
        {
            get
            {
                if (shape != null && shape.IsValid)
                {
                    Rect bounds = shape.LocalBounds();
                    return new Vector2(bounds.width * 0.5f, bounds.height * 0.5f);
                }

                return new Vector2(halfExtentX, halfExtentZ);
            }
        }

        /// <summary>
        /// Arena alanının YEREL uzaydaki merkezi (XZ, metre). Dikdörtgen kipte her zaman sıfırdır;
        /// plan kipinde çokgenin sınırlayıcı kutusunun merkezidir.
        /// <para>
        /// ⚠️ Kadrajlarken <see cref="HalfExtents"/> tek başına yetmez: plan sıfırı bir köşeden
        /// ölçülmüş olabilir, yani kutu transformun tam merkezinde DEĞİLDİR.
        /// </para>
        /// </summary>
        public Vector2 LocalCenter
        {
            get
            {
                if (shape != null && shape.IsValid)
                {
                    Rect bounds = shape.LocalBounds();
                    return bounds.center;
                }

                return Vector2.zero;
            }
        }

        // Gözlemci (admin) kipi: görsel muhafaza susar.
        private bool spectatorMode;
        private float spectatorWallAlpha = 0.25f;

        // Plan önbelleği: kenar dizisi ve kolon dikdörtgenleri kare başına yeniden kurulmasın
        // (Update her karede çalışıyor, tahsis GC baskısı demek).
        private ArenaShapeDefinition cachedShape;
        private Vector2[] cachedOutline;
        private ObstacleRect[] cachedColumns;

        /// <summary>Muhafaza hesabına giren, döndürülmüş bir engel dikdörtgeni (yerel XZ).</summary>
        private struct ObstacleRect
        {
            public Vector2 Center;
            public Vector2 HalfSize;
            public float SinYaw;
            public float CosYaw;
        }

        private void Awake()
        {
            propertyBlock = new MaterialPropertyBlock();
            if (head == null && Camera.main != null)
                head = Camera.main.transform;
            if (warningText != null)
                warningText.gameObject.SetActive(false);
        }

        private void OnEnable()
        {
            RebuildShapeCache();
        }

        /// <summary>
        /// Gözlemci (admin) kipi. Görsel muhafazayı susturur: karartma quad'ı ve alan-dışı
        /// uyarısı kapanır, duvarlar <paramref name="wallAlpha"/>'da sabitlenir,
        /// <see cref="IsOutOfBounds"/> false'a kilitlenir.
        /// <para>
        /// Gerekçe: admin masaüstündedir, HMD'si yoktur; kafası (kapatılmış rig'in
        /// CenterEyeAnchor'ı) sabit durduğu için muhafaza mantığı anlamsız veri üretir. Bileşen
        /// kapatılmak yerine susturulur ki duvarlar gözlemci için istenen saydamlıkta kalsın.
        /// </para>
        /// </summary>
        public void SetSpectatorMode(bool on, float wallAlpha = 0.25f)
        {
            spectatorMode = on;
            spectatorWallAlpha = Mathf.Clamp01(wallAlpha);
            if (!on)
                return; // bir sonraki Update gerçek duruma göre yeniden çizer

            propertyBlock ??= new MaterialPropertyBlock();
            IsOutOfBounds = false;
            SetWallsAlpha(spectatorWallAlpha);
            if (fadeRenderer != null)
                SetAlpha(fadeRenderer, 0f);
            if (warningText != null && warningText.gameObject.activeSelf)
                warningText.gameObject.SetActive(false);
        }

        private void Update()
        {
            if (spectatorMode)
            {
                // Muhafaza susuyor; duvar alfası tercihten gelip sabit kaldığı için iş yok.
                return;
            }

            if (head == null)
                return;

            Vector3 local = transform.InverseTransformPoint(head.position);
            float edgeDistance = EdgeDistance(new Vector2(local.x, local.z));
            IsOutOfBounds = edgeDistance < 0f;

            float warn = Mathf.Clamp01(1f - edgeDistance / warnDistance);
            SetWallsAlpha(Mathf.Lerp(minWallAlpha, maxWallAlpha, warn));

            float outside = Mathf.Clamp01(-edgeDistance / fadeOutsideDistance);
            if (fadeRenderer != null)
                SetAlpha(fadeRenderer, outside * maxFadeAlpha);
            if (warningText != null && warningText.gameObject.activeSelf != IsOutOfBounds)
                warningText.gameObject.SetActive(IsOutOfBounds);
        }

        // -------------------------------------------------------------- mesafe hesabı

        /// <summary>
        /// Verilen YEREL XZ noktasının "en yakın tehlikeye" işaretli uzaklığı: <b>artı</b> = güvenli
        /// alanın içinde ve o kadar metre payı var, <b>eksi</b> = dışarıda (ya da bir engelin
        /// içinde) ve o kadar metre içeri girmiş. Duvar alfası, karartma ve uyarı bu tek sayıdan
        /// türer.
        /// <para>
        /// Plan yoksa eksene hizalı dikdörtgen hesabı (hızlı yol) korunur — mevcut arenaların
        /// tamamı bu yoldan geçer ve davranışları birebir aynıdır.
        /// </para>
        /// </summary>
        private float EdgeDistance(Vector2 point)
        {
            if (cachedShape != shape)
            {
                // Plan çalışma anında değiştirilmiş (ya da OnEnable'dan sonra atanmış) olabilir.
                RebuildShapeCache();
            }

            if (cachedOutline == null)
            {
                return Mathf.Min(halfExtentX - Mathf.Abs(point.x), halfExtentZ - Mathf.Abs(point.y));
            }

            float distance = SignedDistanceToOutline(point);

            if (cachedColumns != null)
            {
                for (int i = 0; i < cachedColumns.Length; i++)
                {
                    distance = Mathf.Min(distance, DistanceToRect(point, cachedColumns[i]));
                }
            }

            // Sahnedeki engeller her karede yeniden okunur: taşınabilir objelerdir, önbelleklenirse
            // sessizce eski yerlerinde uyarı üretirler. Liste indeksle gezilir (foreach bir
            // arayüz numaralandırıcısı kutulardı).
            IReadOnlyList<ArenaObstacle> obstacles = ArenaObstacle.All;
            for (int i = 0; i < obstacles.Count; i++)
            {
                ArenaObstacle obstacle = obstacles[i];
                if (obstacle == null)
                {
                    continue;
                }

                obstacle.GetLocalRect(transform, out Vector2 center, out Vector2 size, out float yaw);
                distance = Mathf.Min(distance, DistanceToRect(point, MakeRect(center, size, yaw)));
            }

            return distance;
        }

        /// <summary>
        /// Noktanın sınır çokgenine işaretli uzaklığı: içerideyse +, dışarıdaysa −; büyüklük her
        /// iki durumda da en yakın KENAR PARÇASINA olan mesafedir (köşe yakınında da doğru sonuç
        /// verir, kenar doğrularına değil parçalarına bakıldığı için).
        /// </summary>
        private float SignedDistanceToOutline(Vector2 point)
        {
            float minDistance = float.MaxValue;
            bool inside = false;

            for (int i = 0, j = cachedOutline.Length - 1; i < cachedOutline.Length; j = i++)
            {
                Vector2 a = cachedOutline[i];
                Vector2 b = cachedOutline[j];

                minDistance = Mathf.Min(minDistance, DistanceToSegment(point, a, b));

                // Ray-casting: noktadan +X yönüne giden ışın kaç kenarı kesiyor (tek = içeride).
                if ((a.y > point.y) != (b.y > point.y) &&
                    point.x < (b.x - a.x) * (point.y - a.y) / (b.y - a.y) + a.x)
                {
                    inside = !inside;
                }
            }

            return inside ? minDistance : -minDistance;
        }

        private static float DistanceToSegment(Vector2 point, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float lengthSq = ab.sqrMagnitude;
            if (lengthSq < 1e-8f)
            {
                return Vector2.Distance(point, a); // dejenere kenar (üst üste iki köşe)
            }

            float t = Mathf.Clamp01(Vector2.Dot(point - a, ab) / lengthSq);
            return Vector2.Distance(point, a + ab * t);
        }

        /// <summary>
        /// Noktanın döndürülmüş bir engel dikdörtgenine işaretli uzaklığı: dışarıdaysa + (kutuya
        /// olan mesafe), içerideyse − (en yakın yüzeye olan derinlik). Sınır hesabıyla aynı işaret
        /// sözleşmesini kullanır, böylece ikisi tek bir <c>Mathf.Min</c> ile birleşebilir.
        /// </summary>
        private static float DistanceToRect(Vector2 point, in ObstacleRect rect)
        {
            // Noktayı dikdörtgenin kendi eksenine taşı (yaw kadar ters döndür).
            Vector2 delta = point - rect.Center;
            Vector2 localPoint = new Vector2(
                delta.x * rect.CosYaw - delta.y * rect.SinYaw,
                delta.x * rect.SinYaw + delta.y * rect.CosYaw);

            float dx = Mathf.Abs(localPoint.x) - rect.HalfSize.x;
            float dy = Mathf.Abs(localPoint.y) - rect.HalfSize.y;

            if (dx > 0f || dy > 0f)
            {
                float outsideX = Mathf.Max(dx, 0f);
                float outsideY = Mathf.Max(dy, 0f);
                return Mathf.Sqrt(outsideX * outsideX + outsideY * outsideY);
            }

            // İçerideyiz: en yakın yüzeye olan mesafe (negatif).
            return Mathf.Max(dx, dy);
        }

        private static ObstacleRect MakeRect(Vector2 center, Vector2 size, float yaw)
        {
            // Ters dönüş açısı saklanır (nokta dikdörtgenin eksenine taşınacak).
            float radians = -yaw * Mathf.Deg2Rad;
            return new ObstacleRect
            {
                Center = center,
                HalfSize = new Vector2(Mathf.Abs(size.x) * 0.5f, Mathf.Abs(size.y) * 0.5f),
                SinYaw = Mathf.Sin(radians),
                CosYaw = Mathf.Cos(radians)
            };
        }

        /// <summary>
        /// Plan önbelleğini kurar. Plan yoksa/geçersizse önbellek boşaltılır ve dikdörtgen hızlı
        /// yolu devreye girer. Kolonlar burada bir kez dikdörtgene çevrilir: asset çalışma anında
        /// değişmez, ama <c>Update</c> her karede koşar.
        /// </summary>
        private void RebuildShapeCache()
        {
            cachedShape = shape;
            cachedOutline = null;
            cachedColumns = null;

            if (shape == null || !shape.IsValid)
            {
                return;
            }

            cachedOutline = shape.Outline;

            if (!shape.ColumnsBlockPlayer)
            {
                return; // kolonlar yalnız geometri: muhafaza onları görmez
            }

            ArenaShapeDefinition.Column[] columns = shape.Columns;
            if (columns == null || columns.Length == 0)
            {
                return;
            }

            cachedColumns = new ObstacleRect[columns.Length];
            for (int i = 0; i < columns.Length; i++)
            {
                cachedColumns[i] = MakeRect(columns[i].center, columns[i].size, columns[i].yaw);
            }
        }

        // ------------------------------------------------------------------ gizmo

        /// <summary>
        /// Seçiliyken planı çizer: sınır çokgeni + kolon kutuları (yerel uzayda hesaplanıp dünyaya
        /// taşınır). Yamuk bir arenayı elle ayarlarken bu çizim şarttır — plan asset'i sayı
        /// listesidir, sahnede karşılığını görmeden köşe taşımak körlemedir.
        /// </summary>
        private void OnDrawGizmosSelected()
        {
            if (shape == null || !shape.IsValid)
            {
                return;
            }

            Vector2[] outline = shape.Outline;
            Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.9f);
            for (int i = 0, j = outline.Length - 1; i < outline.Length; j = i++)
            {
                Gizmos.DrawLine(LocalToWorld(outline[j]), LocalToWorld(outline[i]));
            }

            // Duvar üst kenarı: yüksekliği de gözle doğrulanabilsin.
            Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.35f);
            float wallHeight = shape.WallHeight;
            for (int i = 0, j = outline.Length - 1; i < outline.Length; j = i++)
            {
                Gizmos.DrawLine(LocalToWorld(outline[j], wallHeight), LocalToWorld(outline[i], wallHeight));
                Gizmos.DrawLine(LocalToWorld(outline[i]), LocalToWorld(outline[i], wallHeight));
            }

            ArenaShapeDefinition.Column[] columns = shape.Columns;
            if (columns == null)
            {
                return;
            }

            Gizmos.color = shape.ColumnsBlockPlayer
                ? new Color(0.95f, 0.55f, 0.15f, 0.9f)  // muhafazaya giriyor
                : new Color(0.6f, 0.6f, 0.6f, 0.6f);    // yalnız geometri

            for (int i = 0; i < columns.Length; i++)
            {
                ArenaShapeDefinition.Column column = columns[i];
                float height = shape.HeightOf(column);
                Vector3 center = new Vector3(column.center.x, height * 0.5f, column.center.y);

                Gizmos.matrix = transform.localToWorldMatrix *
                                Matrix4x4.TRS(center, Quaternion.Euler(0f, column.yaw, 0f), Vector3.one);
                Gizmos.DrawWireCube(Vector3.zero, new Vector3(column.size.x, height, column.size.y));
                Gizmos.matrix = Matrix4x4.identity;
            }
        }

        private Vector3 LocalToWorld(Vector2 localPoint, float height = 0f)
        {
            return transform.TransformPoint(new Vector3(localPoint.x, height, localPoint.y));
        }

        private void SetWallsAlpha(float alpha)
        {
            if (wallRenderers == null)
                return;
            foreach (var wall in wallRenderers)
                if (wall != null)
                    SetAlpha(wall, alpha);
        }

        private void SetAlpha(Renderer target, float alpha)
        {
            target.GetPropertyBlock(propertyBlock);
            Color color = target.sharedMaterial != null && target.sharedMaterial.HasProperty(BaseColorId)
                ? target.sharedMaterial.GetColor(BaseColorId)
                : Color.white;
            color.a = alpha;
            propertyBlock.SetColor(BaseColorId, color);
            target.SetPropertyBlock(propertyBlock);
            target.enabled = alpha > 0.001f;
        }
    }
}
