using System;
using UnityEngine;

namespace VortexArena.Core.Combat
{
    /// <summary>
    /// Bir kavrama kaydı: elin <b>KUMANDA ANCHOR'ININ</b> (<c>OVRCameraRig.left/rightHandAnchor</c> —
    /// telde giden el pozunun ta kendisi) eşyaya göre yerel pozu + o elin parmak preset'i.
    /// Kavramanın tek yazılı kaynağı budur ve tanımın (<see cref="ItemDefinition"/>) içinde yaşar —
    /// prefabda kavrama düğümü YOKTUR.
    /// <para>
    /// <b>Kayıt editörde, stüdyoda yazılır</b> (kumanda kökü silahın kabzasına oturtulur, hayalet el
    /// ona bağlı çizilir), gözlükle yakalanmaz. Gerekçe: yakalanan kayıt kaçınılmaz olarak o anki bilek
    /// eğikliğini içeriyordu ve o eğiklik eşyanın kendi dönüşünü sürdüğü için doğrudan namluya
    /// taşınırdı.
    /// </para>
    /// <para>
    /// ⚠️ <b>Uzay ANCHOR'dur, BİLEK DEĞİL.</b> Eşyanın dünya pozunu çözen taraf
    /// (<see cref="ItemGripSolver"/>) da tel de (§6.6) elin ANCHOR pozunu bilir; kayıt aynı uzayda
    /// olduğu için aradaki hiçbir yerde ölçülmüş bir delta gerekmez ve rig'i olmayan izleyici (admin
    /// gözlemci) silahı oyuncuyla BİREBİR aynı çizer. Bunun görünür sonucu: <b>kimlik dönüşlü bir
    /// kayıt = kumandayla hizalı silah</b>. Stüdyoda kökü yalnız taşımak silahın yönüne dokunmaz —
    /// kayıt bilek uzayında tutulsaydı "hiç döndürmedim" bile anchor→bilek deltası kadar (onlarca
    /// derece) dönük bir silah üretirdi. Bilek yalnız GÖRSELİN işidir: sentetik elin bileği ön kabzada
    /// <c>anchor ∘ delta</c>'ya kilitlenir (<c>HandGripPoser</c>), stüdyodaki hayalet el kökten aynı
    /// deltayla ötelenerek çizilir (<c>HandGripConvention.AnchorToWrist</c>).
    /// </para>
    /// <para>
    /// ⚠️ <b>Taşıdığı soru "kumanda eşyanın NERESİNDE ve HANGİ AÇIYLA durur"dur — ikisi de anlamlıdır.</b>
    /// ANA kabzada kayıt <i>eşyayı döndürür</i> (<c>item = anchor ∘ Inverse(kayıt)</c>: el nasıl
    /// tutuyorsa silah öyle durur, el serbest kalır); ÖN kabzada kayıt <i>ikinci eli oturtur</i>
    /// (ikinci elin anchor'ı <c>item ∘ kayıt</c>, sentetik bilek onun deltası kadar ötesine tam
    /// kilitlenir — el ön kabzaya yapışır).
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
        // ⚠️ Ayrı bir "yazıldı" bayrağı ZORUNLU: sıfır poz geçerli bir kavramadır (kumanda tam eşyanın
        // orijininde, eşya kumandayla hizalı — bugünkü varsayılan duruş), yani "hepsi sıfır =
        // yazılmamış" kestirmesi burada sessizce yanlış olurdu. Bayrak, hiç yazılmamış asset'lerde
        // false deserialize edilir.
        [Tooltip("Bu kavrama stüdyoda yazıldı mı. false = hiç yazılmamış (alanların içeriği anlamsız).")]
        public bool authored;

        [Tooltip("Kumanda anchor'ının EŞYAYA göre yerel konumu (metre, ölçeksiz).")]
        public Vector3 position;

        [Tooltip("Kumanda anchor'ının EŞYAYA göre yerel dönüşü (derece, Euler). Ana kabzada eşyayı " +
                 "döndürür (kimlik = kumandayla hizalı silah), ön kabzada ikinci eli oturtur.")]
        public Vector3 euler;

        [Tooltip("Bu slotta elin parmak duruşu (Idle / Firing / Grip).")]
        public HandGripPreset preset;

        /// <summary>Bu kayıt yazıldı mı (yazılmamışsa alanları okunmaz).</summary>
        public bool IsAuthored => authored;

        /// <summary>Anchor'ın eşyaya göre yerel dönüşü.</summary>
        public Quaternion Rotation => Quaternion.Euler(euler);

        /// <summary>Anchor'ın eşyaya göre yerel pozu (konum + dönüş) — tek parça okumak için.</summary>
        public Pose LocalPose => new Pose(position, Rotation);

        /// <summary>
        /// Aynı ölçünün ters yönü: <b>EŞYANIN ANCHOR'a göre</b> pozu. Ana el bunu kullanır —
        /// <c>item = anchor ∘ InverseLocalPose</c>. Yazılmamış (<c>default</c>) kayıtta kimliktir:
        /// eşya kumandanın tam üstünde ve onunla hizalı durur.
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
        /// <param name="anchorInItem">Kumanda anchor'ının EŞYAYA göre yerel pozu — metre, ölçeksiz
        /// (bkz. sınıf uyarısı).</param>
        /// <param name="preset">O slotta elin parmak duruşu.</param>
        public static ItemGripPose From(in Pose anchorInItem, HandGripPreset preset)
        {
            return new ItemGripPose
            {
                authored = true,
                position = anchorInItem.position,
                euler = anchorInItem.rotation.eulerAngles,
                preset = preset,
            };
        }
    }
}
