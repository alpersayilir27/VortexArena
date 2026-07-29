using System.Collections.Generic;
using UnityEngine;

namespace VortexArena.Core.Arena
{
    /// <summary>
    /// Sahneye ELLE konan bir engel (kolon, kasa, direk, sütun) — muhafaza hesabına girsin diye
    /// işaretlenmiş dikdörtgen. Konum ve dönüş objenin kendi transformundan gelir; bileşene
    /// yalnız zemindeki ölçüsü (<see cref="Size"/>) yazılır.
    /// <para>
    /// <b>Neden ayrı bir bileşen:</b> <see cref="ArenaDimensions"/>'daki kolonlar arena
    /// PLANININ parçasıdır (editör aracı geometriyi onlardan üretir). Plana girmeyen, sahneye
    /// serpiştirilen dekor için ise tek doğruluk kaynağı objenin kendisidir — bu bileşen o objeyi
    /// <see cref="ArenaBoundary"/>'ye "buraya yürüme" diye tanıtır.
    /// </para>
    /// <para>
    /// ⚠️ <b>Collider EKLEMEZ, fizik YAPMAZ.</b> Free-roam'da oyuncu fiziksel olarak yürüdüğü için
    /// onu durduran şey gerçek dünyadaki nesnedir, çarpışma değil; bu bileşenin tek işi muhafazanın
    /// (duvar alfası + karartma + uyarı) oyuncuyu engele yaklaşırken uyarmasıdır. Görsel/fiziksel
    /// gövde ayrı objelerdir ve bu bileşen onlara dokunmaz.
    /// </para>
    /// <para>
    /// Kayıt <see cref="OnEnable"/>/<see cref="OnDisable"/> ile yapılır; sahne değişiminde statik
    /// liste kendiliğinden boşalır (sızıntı yok).
    /// </para>
    /// </summary>
    public class ArenaObstacle : MonoBehaviour
    {
        private static readonly List<ArenaObstacle> Registry = new List<ArenaObstacle>();

        [Tooltip("Zemindeki ölçü: X = genişlik, Y = derinlik (metre). Yükseklik önemsizdir — " +
                 "muhafaza 2B çalışır.")]
        [SerializeField] private Vector2 size = new Vector2(1f, 1f);

        /// <summary>Play kipinde aktif olan tüm engeller (salt okunur).
        /// <para>⚠️ Kayıt <c>OnEnable</c>'da dolar, yani <b>yalnız Play kipinde</b> geçerlidir —
        /// editör aracı yazarken sahneyi <c>FindObjectsByType</c> ile tara.</para></summary>
        public static IReadOnlyList<ArenaObstacle> All => Registry;

        /// <summary>Zemindeki ölçü (X = genişlik, Z = derinlik; metre).</summary>
        public Vector2 Size => size;

        private void OnEnable()
        {
            if (!Registry.Contains(this))
            {
                Registry.Add(this);
            }
        }

        private void OnDisable()
        {
            Registry.Remove(this);
        }

        /// <summary>
        /// Engelin dikdörtgenini <paramref name="arenaLocal"/> transformunun YEREL XZ uzayında
        /// verir (<see cref="ArenaBoundary"/> hesabı orada yapılır).
        /// <para>
        /// Dönüşüm burada toplanır çünkü iki transform arasındaki ilişkiyi bilmek gerekir:
        /// merkez nokta dönüşümüyle, <paramref name="yaw"/> ise iki objenin Y dönüşü farkından
        /// gelir. Çağıran yalnız sonucu tüketir.
        /// </para>
        /// ⚠️ Ölçek yok sayılır: engel ölçüsü <see cref="Size"/>'dan gelir, transform scale'inden
        /// değil — aynı prefabın farklı ölçekli kopyaları sessizce farklı muhafaza üretmesin diye
        /// tek doğruluk kaynağı alandır.
        /// </summary>
        /// <param name="arenaLocal">Hesabın yapıldığı uzayın transformu (muhafaza objesi).</param>
        /// <param name="center">Engel merkezi (yerel XZ, metre).</param>
        /// <param name="size">Engel ölçüsü (X = genişlik, Y = derinlik; metre).</param>
        /// <param name="yaw">Engelin yerel uzaydaki Y dönüşü (derece).</param>
        public void GetLocalRect(Transform arenaLocal, out Vector2 center, out Vector2 size, out float yaw)
        {
            size = this.size;

            if (arenaLocal == null)
            {
                center = new Vector2(transform.position.x, transform.position.z);
                yaw = transform.eulerAngles.y;
                return;
            }

            Vector3 local = arenaLocal.InverseTransformPoint(transform.position);
            center = new Vector2(local.x, local.z);

            // Yön farkı: engelin ileri vektörünü yerel uzaya taşıyıp XZ düzleminde açısını al.
            // Euler farkı almak gimbal'a takılır (engel eğik durabilir), yön vektörü takılmaz.
            Vector3 forward = arenaLocal.InverseTransformDirection(transform.forward);
            yaw = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
        }

        // ------------------------------------------------------------------ gizmo

        /// <summary>Editörde yerleştirmeyi kolaylaştırır: zemindeki dikdörtgen + hafif hacim.</summary>
        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.95f, 0.55f, 0.15f, 0.9f);

            // Ölçek bilinçli olarak 1: kutu Size'ı temsil eder, transform scale'ini değil
            // (GetLocalRect de scale'i yok sayıyor — gizmo ile hesap aynı şeyi göstersin).
            Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);
            Gizmos.DrawWireCube(Vector3.zero, new Vector3(size.x, 0.02f, size.y));
            Gizmos.DrawWireCube(new Vector3(0f, 1f, 0f), new Vector3(size.x, 2f, size.y));
            Gizmos.matrix = Matrix4x4.identity;
        }
    }
}
