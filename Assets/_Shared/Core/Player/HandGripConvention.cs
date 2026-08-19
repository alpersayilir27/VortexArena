using UnityEngine;

namespace VortexArena.Core.Player
{
    /// <summary>
    /// "İzleme uzayındaki bir el pozu humanoid el kemiğine nasıl çevrilir" sorusunun TEK cevabı.
    /// <para>
    /// <b>Neden ayrı bir sınıf:</b> ağdan gelen el rotasyonu <c>OVRCameraRig.leftHandAnchor</c> /
    /// <c>rightHandAnchor</c> uzayındadır (kumandanın pozu), kemik ise karakterin bind eksenindedir.
    /// İki uzayın "parmaklar nereye bakar / avuç nereye bakar" tanımı farklıdır; aradaki köprü
    /// yazılmazsa bilek ters çizilir (ölçüldü: sol 115.4°, sağ 128.1° sapma).
    /// </para>
    /// <para>
    /// ⚠️ <b>Kapsamı dardır: gövde BURADAN GEÇMEZ.</b> Kol/bilek zinciri Movement SDK'nın
    /// retargeting'inden geliyor ve SDK kendi eşlemesini kendi yapıyor. Eşyanın eldeki duruşu da
    /// buradan geçmez: kavrama kaydı ANCHOR uzayındadır (<c>ItemGripPose</c>), silahın dünya pozu
    /// deltasız çözülür. Yerel sentetik elin bileğinin kumandaya göre yeri de buradan geçmez
    /// (<c>HandPoseLibrary.AnchorToWrist</c>). Buraya gövdeyle ilgili bir tüketici geri eklenirse,
    /// retargeting ile ikinci bir eşleme kaynağı üretilmiş olur.
    /// </para>
    /// <para>
    /// <b>Bugünkü tek tüketicisi UZAK avatardır</b> (<see cref="HandFingerRig"/> →
    /// <see cref="Correction"/>): ağdan gelen el rotasyonunu humanoid bileğe köprüler.
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
        /// ⚠️ Bu değerler <b>ERGONOMİK TAHMİNDİR, ölçülmüş değildir</b> — <b>UZAK</b> avatarın bileği
        /// eğrik duruyorsa düzeltilecek yer BURASIDIR (başka hiçbir yerde el eksenine dokunulmaz).
        /// ⚠️ <b>Yerel elin bileği buradan GEÇMEZ</b> ve buraya bağlanmaz: o kumandaya kilitlenir
        /// (<c>HandPoseLibrary.AnchorToWrist</c>). Bir zamanlar stüdyodaki hayalet el bu tahminden
        /// çiziliyordu ve parmak ekseni etrafında ~70° sapıyordu.
        /// <b>Nasıl bulunur:</b> <c>ThreePointBodyIK</c>'nın "Bilek eşlemesi (canlı ayar)" alanları
        /// admin (Windows) tarafında CANLI bir uzak avatar üzerinde çevrilir — admin uzak avatarları
        /// çizdiği için ayar APK turu gerektirmez. Oturan değer buraya işlenir ve alan sıfıra döner.
        /// <b>Ölçerek</b> bulmak bu projede mümkün değil: kumandadan sürülen sentetik el
        /// (<c>OVRInput.Controller.LHand/RHand</c> ya da <c>b_*_wrist</c>) ya multimodal
        /// gerektiriyor ya da <c>ControllerModelHider</c> tarafından kapatılmış durumda.
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

        // ⚠️ Anchor→bilek deltası BURADA DEĞİL ve buraya geri eklenmez: o bir eşleme değil bir
        // TANIMDIR (kumandaya kilitlenen bileğin yeri) ve tek sahibi
        // VortexArena.Core.Combat.HandPoseLibrary.AnchorToWrist'tir. Burada durduğu sürece
        // "başlıkta ölçülüp yapıştırılacak sabit" olarak kaldı ve hiç ölçülmediği için tezgâh ile
        // oyun ayrık kaldı.

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

        /// <summary>
        /// Bir iskeletin el anatomisini KENDİ bind pozundan ölçer (el-YEREL uzayda): parmak yönü
        /// el→orta parmak, başparmak yönü el→başparmak, avuç normali ikisinin çapraz çarpımı.
        /// <para>
        /// ⚠️ Çapraz çarpımın sırası ELE GÖRE değişir (aynada simetrik iki iskelet, aynı sıra ters
        /// normal verirdi); kuralın tek yazıldığı yer aşağıdaki <see cref="Vector3"/> aşırı
        /// yüklemesidir ve bu sürüm ona delege eder — başka yere kopyalanmaz.
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

            return TryMeasureBoneBasis(
                hand.InverseTransformDirection(middleProximal.position - hand.position),
                hand.InverseTransformDirection(thumbProximal.position - hand.position),
                rightHand,
                out basis);
        }

        /// <summary>
        /// Aynı ölçüm, yönler <b>hazır</b> verildiğinde (el-YEREL uzayda): kemik <see cref="Transform"/>'u
        /// olmayan iskeletler de (ISDK'nın veri iskeleti, <c>HandPoseLibrary</c>) bu kapıdan geçsin
        /// diye ayrılmıştır.
        /// <para>
        /// ⚠️ Çapraz çarpımın <b>sıra kuralı yalnız BURADA</b> yazılıdır (SOL'da
        /// <c>Cross(thumb, finger)</c>, SAĞ'da <c>Cross(finger, thumb)</c>) ve
        /// <see cref="TryMeasureBoneBasis(Transform, Transform, Transform, bool, out Quaternion)"/>
        /// buna delege eder: iki kopya olsaydı biri düzeltilip öteki unutulur ve bir elin avuç
        /// normali sessizce ters kalırdı.
        /// </para>
        /// </summary>
        public static bool TryMeasureBoneBasis(
            Vector3 fingerDirectionLocal,
            Vector3 thumbDirectionLocal,
            bool rightHand,
            out Quaternion basis)
        {
            basis = Quaternion.identity;

            if (fingerDirectionLocal.sqrMagnitude < MinDirectionSqrMagnitude ||
                thumbDirectionLocal.sqrMagnitude < MinDirectionSqrMagnitude)
            {
                return false;
            }

            Vector3 fingerDirection = fingerDirectionLocal.normalized;
            Vector3 thumbDirection = thumbDirectionLocal.normalized;

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
            return Correction(rightHand, boneBasisLocal, Vector3.zero);
        }

        /// <summary>
        /// Aynı düzeltme, elin <b>anatomik çerçevesinde</b> bir ince ayarla.
        /// <para>
        /// Ayar neden bu çerçevede uygulanıyor: <see cref="AnchorBasis"/> bir
        /// <see cref="Quaternion.LookRotation"/> olduğu için yerel <c>+Z</c> = parmak yönü,
        /// <c>+Y</c> = avuç normalidir. Yani <c>Euler(0, 0, z)</c> <b>parmak ekseni etrafında
        /// roll</b> demektir — anchor anatomisinin analitik olarak en belirsiz terimi tam olarak
        /// budur (bilek doğru yöne bakıp ters yüz durabiliyor) ve tek bir sayıyla aranabilsin diye
        /// ayrı bir eksene düşürülmüştür. <c>X</c> bileği yukarı/aşağı kırar, <c>Y</c> içe/dışa çevirir.
        /// </para>
        /// <para>⚠️ Ayar <b>geçicidir</b>: doğru değer bulununca
        /// <see cref="LeftAnchorFingerDirection"/> ailesine işlenip alan sıfıra döner. İki yerde
        /// birden duran bir sabit er geç birbirinden sapar.</para>
        /// </summary>
        public static Quaternion Correction(bool rightHand, Quaternion boneBasisLocal, Vector3 tuningEuler)
        {
            return AnchorBasis(rightHand) * Quaternion.Euler(tuningEuler) * Quaternion.Inverse(boneBasisLocal);
        }
    }
}
