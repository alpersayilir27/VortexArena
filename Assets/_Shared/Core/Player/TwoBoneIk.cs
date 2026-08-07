using UnityEngine;

namespace VortexArena.Core.Player
{
    /// <summary>
    /// İki kemikli (üst kol → ön kol → el) analitik IK — <b>yalnız ROTASYON yazar</b>.
    /// <para>
    /// ⚠️ <b>Konum yazmamak bir kısıt değil, var olma sebebi.</b> Bileği doğrudan hedefe taşımak
    /// kolun kemik uzunluğunu değiştirir (<c>localPosition</c>) ve iki şeyi birden bozardı:
    /// (1) skinli mesh'te bilek gerilir, (2) <see cref="SkeletonPoseMirror"/> kırmızı takım
    /// gövdesine yalnız <c>localRotation</c> kopyaladığı için ikinci gövde ilk gövdeyi TAKİP ETMEZ —
    /// aynı oyuncu iki takımda iki ayrı duruşta çizilirdi. Rotasyonla çözülen bir hedef ise
    /// kendiliğinden aynalanır.
    /// </para>
    /// <para>
    /// ⚠️ <b>Dirseğin bükülme yönü KORUNUR, dayatılmaz</b> (pole target yok): düzlem normali
    /// kolun <b>o karedeki</b> duruşundan okunuyor. Sabit bir pole hedefi, retargeting'in bulduğu
    /// doğal dirsek yönünü ezerdi ve düzeltme birkaç santimlik olduğu için kazancı da olmazdı.
    /// </para>
    /// <para>
    /// Yöntem kosinüs teoremidir: iki iç açı hedef uzaklığa göre yeniden hesaplanır, sonra üst kol
    /// hedefe nişanlanır. Ulaşılamayan hedefte uzaklık kola <b>kırpılır</b> (kol düz uzanır) —
    /// açık ve sürekli bir davranış, kopma yok.
    /// </para>
    /// </summary>
    public sealed class TwoBoneIk
    {
        /// <summary>Bir kemiğin "var" sayılması için gereken en küçük uzunluk (m).</summary>
        private const float MinBoneLength = 0.02f;

        /// <summary>Tam düz / tam katlanmış tekilliklerinden kaçmak için pay (m).</summary>
        private const float ReachEpsilon = 0.001f;

        private const float MinSqr = 1e-10f;

        private readonly Transform _upper;
        private readonly Transform _lower;
        private readonly Transform _end;
        private readonly float _upperLength;
        private readonly float _lowerLength;

        private TwoBoneIk(Transform upper, Transform lower, Transform end,
            float upperLength, float lowerLength)
        {
            _upper = upper;
            _lower = lower;
            _end = end;
            _upperLength = upperLength;
            _lowerLength = lowerLength;
        }

        /// <summary>Zincirin ucu (bilek kemiği).</summary>
        public Transform End => _end;

        /// <summary>
        /// Uçtan iki ebeveyn yukarı çıkarak zinciri kurar ve kemik uzunluklarını <b>bind pozunda
        /// bir kez</b> ölçer.
        /// <para>⚠️ Uzunluk her karede yeniden ölçülMEZ: IK'nın kendisi kemik uzunluğunu koruduğu
        /// için ölçüm sabit kalmalı; canlı ölçüm, bir kareki hatayı bir sonrakine taşıyıp yavaşça
        /// sürüklenirdi.</para>
        /// <para>Zincir çözülemezse (ebeveyn yok, kemik çok kısa) <c>null</c> döner — çağıran o eli
        /// hiç sürmez. Yarım kurulmuş bir kol, hiç sürülmeyenden daha kötü teşhis edilir.</para>
        /// </summary>
        public static TwoBoneIk TryBuild(Transform end)
        {
            if (end == null || end.parent == null || end.parent.parent == null)
            {
                return null;
            }

            Transform lower = end.parent;
            Transform upper = lower.parent;

            float upperLength = Vector3.Distance(upper.position, lower.position);
            float lowerLength = Vector3.Distance(lower.position, end.position);

            if (upperLength < MinBoneLength || lowerLength < MinBoneLength)
            {
                return null;
            }

            return new TwoBoneIk(upper, lower, end, upperLength, lowerLength);
        }

        /// <summary>
        /// Ucu <paramref name="target"/> noktasına götürür (üst kol + ön kol rotasyonları).
        /// <para>⚠️ Uç kemiğin KENDİ rotasyonuna dokunmaz: onu çağıran yazar (bilek yönü kavramadan
        /// gelir, koldan değil).</para>
        /// </summary>
        public void Solve(Vector3 target)
        {
            if (_upper == null || _lower == null || _end == null)
            {
                return;
            }

            Vector3 a = _upper.position;
            Vector3 b = _lower.position;
            Vector3 c = _end.position;

            Vector3 ab = b - a;
            Vector3 ac = c - a;
            Vector3 at = target - a;

            if (ab.sqrMagnitude < MinSqr || ac.sqrMagnitude < MinSqr || at.sqrMagnitude < MinSqr)
            {
                return;
            }

            float l1 = _upperLength;
            float l2 = _lowerLength;
            float lat = Mathf.Clamp(
                at.magnitude,
                Mathf.Abs(l1 - l2) + ReachEpsilon,
                l1 + l2 - ReachEpsilon);

            // Bugünkü iç açılar…
            float acAb0 = Mathf.Acos(Mathf.Clamp(Vector3.Dot(ac.normalized, ab.normalized), -1f, 1f));
            float baBc0 = Mathf.Acos(Mathf.Clamp(
                Vector3.Dot((a - b).normalized, (c - b).normalized), -1f, 1f));

            // …ve hedef uzaklığın gerektirdiği açılar (kosinüs teoremi).
            float acAb1 = Mathf.Acos(Mathf.Clamp((l2 * l2 - l1 * l1 - lat * lat) / (-2f * l1 * lat), -1f, 1f));
            float baBc1 = Mathf.Acos(Mathf.Clamp((lat * lat - l1 * l1 - l2 * l2) / (-2f * l1 * l2), -1f, 1f));

            // ⚠️ Düzlem normali İKİ dönüşte de AYNI ve dönüşlerden ÖNCE okunur: dirseğin bugünkü
            // bükülme yönü budur ve iki açı düzeltmesi aynı düzlemde kalmak zorunda.
            Vector3 axis = Vector3.Cross(ac, ab);
            if (axis.sqrMagnitude < MinSqr)
            {
                // Kol tam düz: dirsek düzlemi tanımsız. Hedef yönüyle bir düzlem kurulur;
                // o da dejenereyse dokunulmaz (kol zaten hedefe bakıyordur).
                axis = Vector3.Cross(at, ab);
                if (axis.sqrMagnitude < MinSqr)
                {
                    return;
                }
            }

            axis = axis.normalized;

            // ⚠️ Dünya rotasyonuna SOLDAN çarpım: kemiği kendi orijini etrafında `axis` ekseninde
            // döndürür. Ebeveynin dönüşü çocuğu da taşıdığı için sıra üst koldan aşağı olmalı.
            _upper.rotation = Quaternion.AngleAxis((acAb1 - acAb0) * Mathf.Rad2Deg, axis) * _upper.rotation;
            _lower.rotation = Quaternion.AngleAxis((baBc1 - baBc0) * Mathf.Rad2Deg, axis) * _lower.rotation;

            // Uzunluk oturdu; kalan iş kolu hedefe nişanlamak.
            Vector3 aimFrom = _end.position - a;
            if (aimFrom.sqrMagnitude < MinSqr)
            {
                return;
            }

            _upper.rotation = Quaternion.FromToRotation(aimFrom, at) * _upper.rotation;
        }
    }
}
