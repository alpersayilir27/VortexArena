using UnityEngine;

namespace VortexArena.Core.Combat
{
    /// <summary>
    /// Sahne işaretçisi: bir kavrama soketinin <b>yerini, dönüşünü ve yarıçapını sürükleyerek</b>
    /// ayarlamak için. Silahın altına konur (<c>GameObject &gt; VortexArena &gt; Grip Socket
    /// (Primary/Secondary)</c>), kabzaya taşınır, el gibi döndürülür; sonra
    /// <c>Tools &gt; VortexArena &gt; Write Grip Sockets To Definition</c> ile
    /// <see cref="ItemDefinition"/>'a yazılır.
    /// <para>
    /// ⚠️ <b>ÇALIŞMA ANI DAVRANIŞI YOKTUR</b> — ne <c>Awake</c>, ne <c>Update</c>, hiçbir şey. Bu
    /// bir <b>yazma (authoring) aracıdır</b>: oyun bu bileşeni HİÇ okumaz. Kavramanın tek doğruluk
    /// kaynağı <see cref="ItemDefinition"/>'daki alanlardır (soket kapısı da, uzak çizim de oradan
    /// okur). Dolayısıyla işaretçiyi silmek hiçbir şeyi bozmaz, bırakmak da hiçbir şeyi
    /// çalıştırmaz — yalnız Scene view'de bir gizmo olarak durur.
    /// </para>
    /// <para>
    /// Sarı küre = <b>işaretçinin</b> (yani yazılmayı bekleyen değerin) yeri. SO'nun gerçekte ne
    /// dediğini <see cref="ItemGripSockets"/> camgöbeği tel küreyle çizer; ikisi üst üste
    /// oturmuyorsa henüz yazılmamıştır (bkz. o sınıfın notu).
    /// </para>
    /// </summary>
    public class GripSocketMarker : MonoBehaviour
    {
        /// <summary>Dolu kürenin rengi — saydam, altındaki kabza geometrisi görünsün.</summary>
        private static readonly Color FillColor = new Color(1f, 0.92f, 0.16f, 0.25f);

        /// <summary>Tel kürenin rengi — dolu küre saydam olduğu için SINIRI bu çizgi okutuyor.</summary>
        private static readonly Color EdgeColor = new Color(1f, 0.92f, 0.16f, 0.9f);

        [Tooltip("Bu işaretçi hangi el için: Primary (tetik eli) / Secondary (ön kabza).")]
        [SerializeField] private GripSocketKind kind = GripSocketKind.Primary;

        [Tooltip("Soket yarıçapı (m) — Scene view'deki sarı kürenin boyu. SO'ya bu değer yazılır.")]
        [SerializeField] private float radius = 0.12f;

        /// <summary>İşaretçinin türü — yazma aracı hangi SO alanlarına dokunacağını buradan bilir.</summary>
        public GripSocketKind Kind => kind;

        /// <summary>Soket yarıçapı (metre); SO'ya bu değer yazılır.</summary>
        public float Radius => radius;

        // ------------------------------------------------------------------ gizmo

        /// <summary>
        /// ⚠️ <c>OnDrawGizmosSelected</c> DEĞİL: işaretçi ayarlanırken çoğu zaman seçili olan başka
        /// bir şey oluyor (silah kökü, kabza mesh'i) ve soketin nerede olduğu o anda da görünmeli.
        /// Bu yüzden Scene view'de HER ZAMAN çizilir.
        /// </summary>
        private void OnDrawGizmos()
        {
            // ⚠️ Gizmos.matrix KULLANILMAZ: transform ölçeği küreye bulaşır ve yarıçap metre
            // cinsinden okunamaz hale gelir (kavrama ofsetlerinin hiçbiri ölçeklenmiyor —
            // ItemGripSockets.PrimarySocketWorld'deki aynı gerekçe). Dünya konumuyla çiziyoruz.
            Vector3 center = transform.position;
            float r = Mathf.Max(0.01f, radius);

            Gizmos.color = FillColor;
            Gizmos.DrawSphere(center, r);

            Gizmos.color = EdgeColor;
            Gizmos.DrawWireSphere(center, r);

            // Soketin yalnız KONUMU değil DÖNÜŞÜ de SO'ya yazılıyor (el o açıyla girer), bu yüzden
            // eksenler çiziliyor: yönsüz bir küre bilginin yarısını gizler ve kabza doğru yerde
            // ama ters açıyla ayarlanmış olurdu — VR'da bu "silah elde yan duruyor" olarak görünür.
            Gizmos.color = new Color(0.3f, 0.6f, 1f, 0.9f);
            Gizmos.DrawLine(center, center + transform.forward * r);

            Gizmos.color = new Color(0.35f, 1f, 0.4f, 0.9f);
            Gizmos.DrawLine(center, center + transform.up * r);

            Gizmos.color = new Color(1f, 0.35f, 0.3f, 0.9f);
            Gizmos.DrawLine(center, center + transform.right * r);
        }
    }
}
