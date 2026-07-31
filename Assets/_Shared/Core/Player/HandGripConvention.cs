using UnityEngine;

namespace VortexArena.Core.Player
{
    /// <summary>
    /// "İzleme uzayındaki bir el pozu humanoid el kemiğine nasıl çevrilir" sorusunun TEK cevabı.
    /// <para>
    /// <b>Neden ayrı bir sınıf:</b> ağdan gelen el rotasyonu <c>OVRCameraRig.leftHandAnchor</c> /
    /// <c>rightHandAnchor</c> uzayındadır (kumandanın pozu), kemik ise karakterin bind eksenindedir.
    /// İki uzayın "parmaklar nereye bakar / avuç nereye bakar" tanımı farklıdır; aradaki köprü
    /// yazılmazsa bilek ters çizilir (ölçüldü: sol 115.4°, sağ 128.1° sapma). Aynı köprüye uzak
    /// avatar (<see cref="ThreePointBodyIK"/>) ve yerel kol görseli birlikte ihtiyaç duyduğu için
    /// dönüşüm tek yerde durur.
    /// </para>
    /// <para>
    /// <b>Türetme:</b> iki iskeletin de aynı anatomik yöne bakması istenir, yani
    /// <c>hand.rotation * boneBasis == anchorRotation * anchorBasis</c> →
    /// <c>hand.rotation = anchorRotation * (anchorBasis * Inverse(boneBasis))</c>. Parantez içi
    /// <see cref="Correction"/>'dır ve karakter başına BİR KEZ, bind pozunda hesaplanır.
    /// </para>
    /// <para>
    /// ⚠️ Kemik tarafı SABİT YAZILMAZ, <see cref="TryMeasureBoneBasis"/> ile çalışma anında
    /// ölçülür: karakter değiştiğinde (Mixamo'dan başka bir rig'e geçilse bile) burada tek satır
    /// değişmesin diye. Sabit olan yalnız izleme tarafıdır — o donanımdan gelir, modelden değil.
    /// </para>
    /// </summary>
    public static class HandGripConvention
    {
        /// <summary>
        /// Kumanda anchor'ı uzayında elin anatomisi — <b>TEK AYAR NOKTASI</b>.
        /// Parmaklar kumandanın ilerisine ve hafif aşağı bakar; avuç içe (gövde orta hattına) ve
        /// hafif yukarı bakar.
        /// <para>
        /// ⚠️ Bu değerler <b>ERGONOMİK TAHMİNDİR, ölçülmüş değildir.</b> Kesin değeri
        /// <see cref="HandGripCalibrationProbe"/> cihazda ölçüp yapıştırılabilir biçimde loglar;
        /// bilek hâlâ eğrik duruyorsa düzeltilecek yer BURASIDIR (başka hiçbir yerde el eksenine
        /// dokunulmaz).
        /// </para>
        /// <para>
        /// İki vektörün birbirine dik olması gerekmez: <see cref="Quaternion.LookRotation"/> ikinci
        /// vektörü birinciye göre diklerştirir.
        /// </para>
        /// </summary>
        public static readonly Vector3 LeftAnchorFingerDirection = new Vector3(0f, -0.42f, 0.91f);
        public static readonly Vector3 LeftAnchorPalmNormal = new Vector3(0.87f, 0.50f, 0f);
        public static readonly Vector3 RightAnchorFingerDirection = new Vector3(0f, -0.42f, 0.91f);
        public static readonly Vector3 RightAnchorPalmNormal = new Vector3(-0.87f, 0.50f, 0f);

        /// <summary>
        /// Meta OVR el iskeletinin bilek kemiği (<c>b_*_wrist</c>) anatomisi, BİLEK-YEREL uzayda —
        /// ölçülmüş sabitler.
        /// <para>Yalnız <see cref="HandGripCalibrationProbe"/> kullanır: anchor→bilek dönüşü ölçülüp
        /// bu vektörlerle çarpılınca yukarıdaki tahmini sabitlerin gerçek karşılığı çıkar.</para>
        /// </summary>
        public static readonly Vector3 LeftOvrWristFingerDirection = new Vector3(-1f, 0f, 0f);
        public static readonly Vector3 LeftOvrWristPalmNormal = new Vector3(0f, 0.83f, 0.55f);
        public static readonly Vector3 RightOvrWristFingerDirection = new Vector3(1f, 0f, 0f);
        public static readonly Vector3 RightOvrWristPalmNormal = new Vector3(0f, -0.83f, -0.55f);

        /// <summary>Yön vektörünün "anlamlı" sayılması için gereken en küçük kare uzunluk.</summary>
        private const float MinDirectionSqrMagnitude = 1e-8f;

        /// <summary>Parmak/başparmak yönü paralelleşirse avuç normali üretilemez (cross ≈ 0).</summary>
        private const float MinCrossSqrMagnitude = 1e-6f;

        /// <summary>Kumanda anchor'ı uzayındaki anatomik baz.</summary>
        public static Quaternion AnchorBasis(bool rightHand)
        {
            return rightHand
                ? Quaternion.LookRotation(RightAnchorFingerDirection, RightAnchorPalmNormal)
                : Quaternion.LookRotation(LeftAnchorFingerDirection, LeftAnchorPalmNormal);
        }

        /// <summary>OVR bilek kemiği uzayındaki anatomik baz (probe için).</summary>
        public static Quaternion OvrWristBasis(bool rightHand)
        {
            return rightHand
                ? Quaternion.LookRotation(RightOvrWristFingerDirection, RightOvrWristPalmNormal)
                : Quaternion.LookRotation(LeftOvrWristFingerDirection, LeftOvrWristPalmNormal);
        }

        /// <summary>OVR bileğinin parmak yönü (bilek-yerel).</summary>
        public static Vector3 OvrWristFingerDirection(bool rightHand)
        {
            return rightHand ? RightOvrWristFingerDirection : LeftOvrWristFingerDirection;
        }

        /// <summary>OVR bileğinin avuç normali (bilek-yerel).</summary>
        public static Vector3 OvrWristPalmNormal(bool rightHand)
        {
            return rightHand ? RightOvrWristPalmNormal : LeftOvrWristPalmNormal;
        }

        /// <summary>
        /// Bir iskeletin el anatomisini KENDİ bind pozundan ölçer (el-YEREL uzayda): parmak yönü
        /// el→orta parmak, başparmak yönü el→başparmak, avuç normali ikisinin çapraz çarpımı.
        /// <para>
        /// ⚠️ Çapraz çarpımın sırası ELE GÖRE değişir (aynada simetrik iki iskelet, aynı sıra ters
        /// normal verirdi): SOL'da <c>Cross(thumb, finger)</c>, SAĞ'da <c>Cross(finger, thumb)</c>.
        /// Bu kuralın projedeki TEK uygulaması burasıdır — başka yere kopyalanmaz.
        /// </para>
        /// <para>
        /// ⚠️ <b>Bind pozunda çağrılmalıdır</b> (çözücü kemiklere yazmadan ÖNCE): poz bozulduktan
        /// sonra ölçülen baz o karenin duruşunu içerir ve düzeltme kalıcı olarak yanlış çıkar.
        /// </para>
        /// <para>Parmak kemikleri humanoid'de İSTEĞE BAĞLIDIR; yoksa ya da yönler dejenereyse
        /// <c>false</c> döner — çağıran düzeltmeyi kimlik bırakıp uyarı basar (açık başarısızlık).</para>
        /// </summary>
        public static bool TryMeasureBoneBasis(
            Transform hand,
            Transform middleProximal,
            Transform thumbProximal,
            bool rightHand,
            out Quaternion basis)
        {
            basis = Quaternion.identity;

            if (hand == null || middleProximal == null || thumbProximal == null)
            {
                return false;
            }

            Vector3 fingerDirection = hand.InverseTransformDirection(
                middleProximal.position - hand.position);
            Vector3 thumbDirection = hand.InverseTransformDirection(
                thumbProximal.position - hand.position);

            if (fingerDirection.sqrMagnitude < MinDirectionSqrMagnitude ||
                thumbDirection.sqrMagnitude < MinDirectionSqrMagnitude)
            {
                return false;
            }

            fingerDirection = fingerDirection.normalized;
            thumbDirection = thumbDirection.normalized;

            Vector3 palmNormal = rightHand
                ? Vector3.Cross(fingerDirection, thumbDirection)
                : Vector3.Cross(thumbDirection, fingerDirection);

            if (palmNormal.sqrMagnitude < MinCrossSqrMagnitude)
            {
                return false;
            }

            basis = Quaternion.LookRotation(fingerDirection, palmNormal.normalized);
            return true;
        }

        /// <summary>
        /// İzleme uzayındaki rotasyonun SAĞINA çarpılacak düzeltme:
        /// <c>hand.rotation = anchorRotation * Correction(...)</c>.
        /// </summary>
        public static Quaternion Correction(bool rightHand, Quaternion boneBasisLocal)
        {
            return AnchorBasis(rightHand) * Quaternion.Inverse(boneBasisLocal);
        }
    }
}
