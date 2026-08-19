using System;
using UnityEngine;

namespace VortexArena.Core.Combat
{
    /// <summary>
    /// Bir kavrama kaydı: elin <b>KUMANDA ANCHOR'ININ</b> (<c>OVRCameraRig.left/rightHandAnchor</c> —
    /// telde giden el pozunun ta kendisi) eşyaya göre yerel <b>KONUMU</b> + o el için <b>bu silaha
    /// özel riglenmiş parmak duruşu</b> + <b>el modelinin kumanda üstündeki yerleşimi</b>. Kavramanın
    /// tek yazılı kaynağı budur ve tanımın (<see cref="ItemDefinition"/>) içinde yaşar — prefabda
    /// kavrama düğümü YOKTUR.
    /// <para>
    /// ⚠️ <b>ANCHOR kaydının (<see cref="position"/>) DÖNÜŞÜ YOKTUR ve eklenmez.</b> Eşyanın dönüşü
    /// her zaman ana kumandanın dönüşüdür (<see cref="ItemGripSolver"/>): o alan yalnız "kumanda
    /// eşyanın NERESİNDE durur" der. Bir dönüş taşısaydı stüdyoda kökü çeviren herkes oyunda silahı
    /// kumandadan saptırırdı — sahada belirtisi "el konumuna göre silah bozuk geliyor"dur ve teşhisi
    /// pahalıdır. Ön kabzada da aynı: ikinci elin kumandası eşyayla hizalı sayılır, sentetik bilek
    /// onun anchor→bilek deltası kadar ötesine kilitlenir.
    /// </para>
    /// <para>
    /// ⚠️ <b>Bilek dönüşü (<see cref="wristRotation"/>) bunun İSTİSNASI DEĞİL, BAŞKA BİR ŞEYİDİR:</b>
    /// eşyanın nereye gideceğini değil, <b>el modelinin kumandanın üstünde nasıl duracağını</b>
    /// söyler. İkisi bilerek ayrı: silah her zaman kumandayla hizalı kalır, el ise silaha göre yan
    /// (ya da alttan) durabilir — çoğu kabza ile ön kabza aynı eli aynı açıyla kabul etmiyor. Bu
    /// alanları değiştirmek eşyanın pozunu HİÇBİR ŞEKİLDE etkilemez; okuyanı tek bir yerdir
    /// (<c>ItemGripAuthority.ResolveAnchorToWrist</c>).
    /// </para>
    /// <para>
    /// <b>Kayıt editörde, stüdyoda yazılır</b> (kumanda kökü silahın kabzasına oturtulur), gözlükle
    /// yakalanmaz. Uzay ANCHOR'dur: eşyanın dünya pozunu çözen taraf da tel de elin ANCHOR pozunu
    /// bilir, kayıt aynı uzayda olduğu için hiçbir yerde delta gerekmez ve rig'i olmayan izleyici
    /// (admin gözlemci) silahı oyuncuyla birebir aynı çizer.
    /// </para>
    /// <para>
    /// ⚠️ <b><see cref="position"/> METREdir ve eşyanın GÖRSEL ÖLÇEĞİYLE BÜYÜTÜLMEZ.</b> Geri
    /// bileşim her zaman <c>item.position + item.rotation * position</c> ile yazılır,
    /// <c>Transform.TransformPoint</c> ile DEĞİL: <c>WPN_*</c> kökleri 0.8 ölçekli, yani
    /// <c>TransformPoint</c> aynı ölçüyü ikinci kez uygular ve el silahın yanında yüzer. Stüdyo da
    /// aynı simetrik yolla (elle ters bileşim) yazar — iki uç tek sözleşmede kalsın.
    /// </para>
    /// </summary>
    [Serializable]
    public struct ItemGripPose
    {
        // ⚠️ Ayrı bir "yazıldı" bayrağı ZORUNLU: sıfır konum geçerli bir kavramadır (kumanda tam eşyanın
        // orijininde — bugünkü varsayılan duruş), yani "sıfır = yazılmamış" kestirmesi burada sessizce
        // yanlış olurdu. Bayrak, hiç yazılmamış asset'lerde false deserialize edilir.
        [Tooltip("Bu kavrama stüdyoda yazıldı mı. false = hiç yazılmamış (alanların içeriği anlamsız).")]
        public bool authored;

        [Tooltip("Kumanda anchor'ının EŞYAYA göre yerel konumu (metre, ölçeksiz). Dönüş yoktur: silah her " +
                 "zaman kumandayla hizalıdır.")]
        public Vector3 position;

        // ⚠️ Parmak duruşu SEÇİLMEZ, RİGLENİR: bu dizi stüdyoda elle çevrilmiş parmak eklemlerinin
        // kaydıdır ve silah başınadır (hazır bir "sıkma/kabza" tablosu YOKTUR — kabzaların geometrisi
        // birbirini tutmadığı için ortak tablo bazı silahlarda parmakları gövdenin içinde bırakıyordu).
        // ⚠️ Dizi BOŞ olabilir: konumu yazılmış ama parmakları henüz riglenmemiş bir slot geçerlidir
        // ve o el boş elin duruşunda kalır (HandPoseLibrary.IdleJointRotations).
        [Tooltip("Bu slotta elin riglenmiş parmak duruşu — eklem başına yerel dönüş. Boş = riglenmemiş " +
                 "(el boşta duruşunda kalır).")]
        public HandJointRotation[] fingerJoints;

        // ⚠️ Bileğin de ayrı bir "yazıldı" bayrağı var ve ZORUNLU: bu alanlar kavrama kaydından SONRA
        // eklendi, yani diskteki eski kayıtlarda anahtarları hiç yok ve wristRotation (0,0,0,0) olarak
        // deserialize ediliyor — geçersiz bir quaternion. Bayrak düşükken okuma yolu paylaşılan
        // varsayılan ofsete düşer (HandPoseLibrary.AnchorToWrist), yani eski silahlar bu alan
        // yazılana kadar bugünkü ellerini aynen korur.
        [Tooltip("El modelinin bu slottaki yerleşimi stüdyoda yazıldı mı. false = yazılmamış " +
                 "(paylaşılan varsayılan ofset kullanılır).")]
        public bool wristAuthored;

        [Tooltip("El modelinin (ISDK bileğinin) KUMANDA ANCHOR'ına göre yerel konumu (metre).")]
        public Vector3 wristPosition;

        [Tooltip("El modelinin KUMANDA ANCHOR'ına göre yerel dönüşü — elin silaha göre yan/alttan " +
                 "durmasını bu taşır. Eşyanın pozunu ETKİLEMEZ.")]
        public Quaternion wristRotation;

        /// <summary>Bu kayıt yazıldı mı (yazılmamışsa alanları okunmaz).</summary>
        public bool IsAuthored => authored;

        /// <summary>Bu slotun parmakları riglendi mi (konum yazılmış olsa bile boş olabilir).</summary>
        public bool HasFingers => fingerJoints != null && fingerJoints.Length > 0;

        /// <summary>
        /// El modelinin yerleşimi bu slotta yazılmış mı.
        /// <para>⚠️ Bayrağın yanında dönüşün <b>geçerliliği</b> de sınanır: bayrak elle
        /// (asset'te/merge'de) açılmış ama dönüş sıfır kalmış bir kayıt, eli görünmez/bozuk bir
        /// dönüşle çizerdi — bu sınama onu sessizce varsayılana düşürür.</para>
        /// </summary>
        public bool HasWrist =>
            wristAuthored && Quaternion.Dot(wristRotation, wristRotation) > 0.0001f;

        /// <summary>Yazılmış el yerleşimi (yalnız <see cref="HasWrist"/> iken anlamlı).</summary>
        public Pose Wrist => new Pose(wristPosition, wristRotation);

        /// <summary>
        /// Stüdyoda yazılmış bir konum + el yerleşimi + parmak duruşundan kayıt üretir
        /// (<see cref="authored"/> = <c>true</c>).
        /// </summary>
        /// <param name="anchorInItem">Kumanda anchor'ının EŞYAYA göre yerel konumu — metre, ölçeksiz
        /// (bkz. sınıf uyarısı).</param>
        /// <param name="wristInAnchor">El modelinin kumanda anchor'ına göre yerel pozu.</param>
        /// <param name="fingerJoints">O slotta riglenmiş parmak eklemleri (boş olabilir).</param>
        public static ItemGripPose From(in Vector3 anchorInItem, in Pose wristInAnchor,
            HandJointRotation[] fingerJoints)
        {
            return new ItemGripPose
            {
                authored = true,
                position = anchorInItem,
                fingerJoints = fingerJoints,
                wristAuthored = true,
                wristPosition = wristInAnchor.position,
                wristRotation = wristInAnchor.rotation,
            };
        }
    }
}
