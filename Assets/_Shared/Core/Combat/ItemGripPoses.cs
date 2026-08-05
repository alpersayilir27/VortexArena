using Oculus.Interaction.HandGrab;
using UnityEngine;

namespace VortexArena.Core.Combat
{
    /// <summary>
    /// Kavrama pozlarının prefabdaki <b>yerinin ve adının TEK tanımı</b>: pozu üreten editör aracı da,
    /// onu ele uygulayan <see cref="HandGripPoser"/> de buradan geçer.
    /// <para>
    /// ⚠️ <b>Ad iki yerde ayrı yazılmaz.</b> Üretici ile tüketici kendi string'ini taşısaydı bir
    /// harflik sapma hiçbir hata üretmez, yalnız poz sessizce <b>bulunamaz</b> olurdu: silah elde
    /// durur, parmaklar sarılmaz ve belirti "araç çalışmadı" gibi görünürdü.
    /// </para>
    /// <para>
    /// Yerleşim iki seviyedir: <c>&lt;silah kökü&gt;/GripPoses/Pose_&lt;Kind&gt;_&lt;L|R&gt;</c>.
    /// </para>
    /// </summary>
    public static class ItemGripPoses
    {
        /// <summary>Pozları toplayan düğümün adı (silah kökünün DOĞRUDAN çocuğu).</summary>
        public const string RootNodeName = "GripPoses";

        /// <summary>
        /// Bir kavrama noktası + el bileşiminin düğüm adı: <c>Pose_Primary_R</c>, <c>Pose_Secondary_L</c> …
        /// </summary>
        public static string NodeName(GripSocketKind kind, bool rightHand)
        {
            return $"Pose_{kind}_{(rightHand ? "R" : "L")}";
        }

        /// <summary>
        /// Silahın altındaki kavrama pozunu bulur; yoksa (ya da poz verisi taşımıyorsa) <c>null</c>.
        /// <para>
        /// ⚠️ <b>Ağaç taranmaz</b> (<c>GetComponentsInChildren</c> DEĞİL): arama tam iki seviye
        /// <see cref="Transform.Find"/> ile iner. Aksi hâlde silahın altına ileride konacak başka bir
        /// <see cref="HandGrabPose"/> (aksesuar, nişangâh, çerçeve) yanlışlıkla ana kavrama pozu
        /// sanılır ve el silahın ortasına sarılırdı. <c>Transform.Find</c> pasif çocukları da bulur,
        /// yani poz düğümleri kapalı tutulabilir.
        /// </para>
        /// <para>
        /// <b><see cref="HandGrabPose.UsesHandPose"/> false ise <c>null</c> dönülür:</b> pozu olmayan
        /// bir düğüm yalnız bir konum işaretçisidir, parmaklara yazacak verisi yoktur — onu "bulundu"
        /// saymak <c>HandPose</c> üstünde null referansa düşerdi.
        /// </para>
        /// <para>
        /// ⚠️ <b>YERLEŞTİRİLMEMİŞ düğüm de <c>null</c> sayılır</b> (<see cref="IsUnplaced"/>).
        /// <c>WeaponKitBuilder</c> düğümleri her silahta <b>sıfır transformla</b> açıyor — yani araç
        /// koştuğu anda henüz hiçbir poz yazılmamış olsa da ortada dört düğüm bulunur. Bu kapı
        /// olmasaydı el, kavrama noktasına değil <b>silahın orijinine</b> kilitlenir ve nötr bind
        /// duruşunda donardı; yani araç çalıştırmak, poz yazmadan önce bugünkü davranışı BOZARDI.
        /// Kapının anlamı dar ve tek: "bu düğüm henüz kabzaya taşınmadı". Kullanıcı düğümü yerine
        /// sürüklediği an poz kendiliğinden devreye girer, ayrıca bir onay adımı gerekmez.
        /// </para>
        /// </summary>
        public static HandGrabPose Find(Transform itemRoot, GripSocketKind kind, bool rightHand)
        {
            if (itemRoot == null)
            {
                return null;
            }

            Transform root = itemRoot.Find(RootNodeName);
            if (root == null)
            {
                return null;
            }

            Transform node = root.Find(NodeName(kind, rightHand));
            if (node == null)
            {
                return null;
            }

            var pose = node.GetComponent<HandGrabPose>();
            if (pose == null || !pose.UsesHandPose() || pose.HandPose == null)
            {
                return null;
            }

            return IsUnplaced(pose) ? null : pose;
        }

        /// <summary>
        /// Düğüm hâlâ üretildiği yerde mi (referansına göre birim poz) — yani <b>kabzaya hiç
        /// taşınmamış</b> mı.
        /// <para>
        /// Ölçü, tüketicinin gerçekten kullandığı büyüklük üzerinden alınır
        /// (<see cref="HandGrabPose.RelativePose"/>), düğümün ham <c>localPosition</c>'ı üzerinden
        /// değil: araya bir ara düğüm (<c>GripPoses</c>) girdiği için ikisi ayrışabilir ve kapı o
        /// zaman yanlış soruyu cevaplardı.
        /// </para>
        /// <para>
        /// ⚠️ Eşikler DAR tutulur (1 mm / 0.5°): amaç "yerleştirilmemiş"i ayırmak, "orijine yakın"ı
        /// elemek değil. Gerçek bir kavrama pozunun bileği silahın orijininde ve dönüşsüz olamaz —
        /// orası tüfekte gövdenin içidir.
        /// </para>
        /// </summary>
        public static bool IsUnplaced(HandGrabPose pose)
        {
            Pose relative = pose.RelativePose;

            return relative.position.sqrMagnitude < 1e-6f &&
                   Quaternion.Angle(relative.rotation, Quaternion.identity) < 0.5f;
        }
    }
}
