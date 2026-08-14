using System;
using UnityEngine;

namespace VortexArena.Core.Combat
{
    /// <summary>
    /// VR'da <b>yakalanmış</b> bir kavrama: elin (ISDK BİLEĞİNİN) eşyaya göre yerel pozu.
    /// Kavramanın tek yazılı kaynağı budur ve tanımın (<see cref="ItemDefinition"/>) içinde yaşar —
    /// prefabda kavrama düğümü YOKTUR.
    /// <para>
    /// ⚠️ <b><see cref="position"/> METREdir ve eşyanın GÖRSEL ÖLÇEĞİYLE BÜYÜTÜLMEZ.</b> Geri
    /// bileşim her zaman <c>item.position + item.rotation * position</c> ile yazılır,
    /// <c>Transform.TransformPoint</c> ile DEĞİL: <c>WPN_*</c> kökleri 0.8 ölçekli, yani
    /// <c>TransformPoint</c> aynı ölçüyü ikinci kez uygular ve el silahın yanında yüzer.
    /// Yakalama da aynı simetrik yolla (elle ters bileşim) alınır — iki uç tek sözleşmede kalsın.
    /// </para>
    /// <para>
    /// ⚠️ <b>Taşıdığı soru "el eşyanın NERESİNDE durur"dur — "hangi AÇIYLA" değil.</b> Eşyanın
    /// eldeki dönüşü kimliktir (eksenleri kumanda anchor'ıyla birebir aynı, bkz.
    /// <see cref="ItemDefinition.PrimaryGripRotation"/>) ve elin bileğine de dönüş YAZILMAZ:
    /// <c>HandGripPoser</c> sentetik elin yalnız KONUMUNU kilitler, dönüşü kumandayla birlikte
    /// serbest döner. Böylece kumanda nereye bakıyorsa el ve namlu oraya bakar, ikisi tek parça
    /// gibi durur.
    /// </para>
    /// <para>
    /// ⚠️ <b>Bu yüzden <see cref="euler"/> ANA kabzada hiçbir şeyi sürmez</b> ve onu tüketen bir
    /// yol eklenmemelidir: elin açısını yakalamadan almak, oyuncunun kalibrasyon anındaki bilek
    /// eğikliğini kalıcı hale getirir — kumandayı dosdoğru ileri tutarken el yamuk görünür ve
    /// silah, dosdoğru duruyor olmasına rağmen yamuk tutuluyormuş gibi okunur. Alan yine de
    /// yazılır ve saklanır: ön kabzada uzak avatarın ikinci el hedefi onu okuyor
    /// (<see cref="ItemDefinition.SecondaryGripRotation"/>), ve iki aşamanın aynı kaydı yazması
    /// aracın tek bir yakalama yolu olmasını sağlıyor.
    /// </para>
    /// </summary>
    [Serializable]
    public struct ItemGripCapture
    {
        // ⚠️ Ayrı bir "yakalandı" bayrağı ZORUNLU: sıfır poz geçerli bir kavrama olabilir (bileği
        // tam eşyanın orijininde olan bir tutuş), yani "hepsi sıfır = yazılmamış" kestirmesi burada
        // sessizce yanlış olurdu. Bayrak, hiç yakalanmamış eski asset'lerde false deserialize edilir.
        [Tooltip("Bu kavrama VR'da yakalandı mı. false = hiç yazılmamış (alanların içeriği anlamsız).")]
        public bool captured;

        [Tooltip("Elin (ISDK bileğinin) EŞYAYA göre yerel konumu (metre, ölçeksiz).")]
        public Vector3 position;

        [Tooltip("Elin (ISDK bileğinin) EŞYAYA göre yerel dönüşü (derece, Euler). Ana kabzada " +
                 "hiçbir şeyi sürmez (el kumandayla döner); ön kabzada uzak avatarın ikinci el " +
                 "hedefi okur.")]
        public Vector3 euler;

        /// <summary>Bu kayıt VR'da yakalandı mı (yakalanmamışsa alanları okunmaz).</summary>
        public bool IsCaptured => captured;

        /// <summary>Bileğin eşyaya göre yerel dönüşü.</summary>
        public Quaternion Rotation => Quaternion.Euler(euler);

        /// <summary>Bileğin eşyaya göre yerel pozu (konum + dönüş) — tek parça okumak için.</summary>
        public Pose LocalPose => new Pose(position, Rotation);

        /// <summary>
        /// Yakalanmış bir pozdan kayıt üretir (<see cref="captured"/> = <c>true</c>).
        /// </summary>
        /// <param name="itemLocal">Bileğin EŞYAYA göre yerel pozu — metre, ölçeksiz
        /// (bkz. sınıf uyarısı).</param>
        public static ItemGripCapture From(in Pose itemLocal)
        {
            return new ItemGripCapture
            {
                captured = true,
                position = itemLocal.position,
                euler = itemLocal.rotation.eulerAngles,
            };
        }
    }
}
