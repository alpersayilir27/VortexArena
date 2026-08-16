using System;
using UnityEngine;

namespace VortexArena.Core.Combat
{
    /// <summary>
    /// Bir kavrama kaydı: elin (ISDK <b>BİLEĞİNİN</b>) eşyaya göre yerel pozu + o elin parmak
    /// preset'i. Kavramanın tek yazılı kaynağı budur ve tanımın (<see cref="ItemDefinition"/>)
    /// içinde yaşar — prefabda kavrama düğümü YOKTUR.
    /// <para>
    /// <b>Kayıt editörde, stüdyoda yazılır</b> (hayalet el silahın kabzasına oturtulur), gözlükle
    /// yakalanmaz. Gerekçe: yakalanan kayıt kaçınılmaz olarak o anki bilek eğikliğini içeriyordu ve
    /// o eğiklik artık eşyanın kendi dönüşünü sürdüğü için doğrudan namluya taşınırdı.
    /// </para>
    /// <para>
    /// ⚠️ <b>Taşıdığı soru "el eşyanın NERESİNDE ve HANGİ AÇIYLA durur"dur — ikisi de anlamlıdır.</b>
    /// ANA kabzada kayıt <i>eşyayı döndürür</i> (<c>item = bilek ∘ Inverse(kayıt)</c>: el nasıl
    /// tutuyorsa silah öyle durur, bilek serbest kalır); ÖN kabzada kayıt <i>bileği oturtur</i>
    /// (sentetik el <c>item ∘ kayıt</c> pozuna tam kilitlenir, el ön kabzaya yapışır).
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
        // ⚠️ Ayrı bir "yazıldı" bayrağı ZORUNLU: sıfır poz geçerli bir kavrama olabilir (bileği
        // tam eşyanın orijininde olan bir tutuş), yani "hepsi sıfır = yazılmamış" kestirmesi burada
        // sessizce yanlış olurdu. Bayrak, hiç yazılmamış asset'lerde false deserialize edilir.
        [Tooltip("Bu kavrama stüdyoda yazıldı mı. false = hiç yazılmamış (alanların içeriği anlamsız).")]
        public bool authored;

        [Tooltip("Elin (ISDK bileğinin) EŞYAYA göre yerel konumu (metre, ölçeksiz).")]
        public Vector3 position;

        [Tooltip("Elin (ISDK bileğinin) EŞYAYA göre yerel dönüşü (derece, Euler). Ana kabzada " +
                 "eşyayı döndürür, ön kabzada bileği oturtur.")]
        public Vector3 euler;

        [Tooltip("Bu slotta elin parmak duruşu (Idle / Firing / Grip).")]
        public HandGripPreset preset;

        /// <summary>Bu kayıt yazıldı mı (yazılmamışsa alanları okunmaz).</summary>
        public bool IsAuthored => authored;

        /// <summary>Bileğin eşyaya göre yerel dönüşü.</summary>
        public Quaternion Rotation => Quaternion.Euler(euler);

        /// <summary>Bileğin eşyaya göre yerel pozu (konum + dönüş) — tek parça okumak için.</summary>
        public Pose LocalPose => new Pose(position, Rotation);

        /// <summary>
        /// Aynı ölçünün ters yönü: <b>EŞYANIN BİLEĞE göre</b> pozu. Ana el bunu kullanır —
        /// <c>item = bilek ∘ InverseLocalPose</c>.
        /// </summary>
        public Pose InverseLocalPose
        {
            get
            {
                Quaternion inverse = Quaternion.Inverse(Rotation);
                return new Pose(inverse * (-position), inverse);
            }
        }

        /// <summary>
        /// Stüdyoda yazılmış bir pozdan kayıt üretir (<see cref="authored"/> = <c>true</c>).
        /// </summary>
        /// <param name="wristInItem">Bileğin EŞYAYA göre yerel pozu — metre, ölçeksiz
        /// (bkz. sınıf uyarısı).</param>
        /// <param name="preset">O slotta elin parmak duruşu.</param>
        public static ItemGripPose From(in Pose wristInItem, HandGripPreset preset)
        {
            return new ItemGripPose
            {
                authored = true,
                position = wristInItem.position,
                euler = wristInItem.rotation.eulerAngles,
                preset = preset,
            };
        }
    }
}
