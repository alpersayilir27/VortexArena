using System;
using UnityEngine;

namespace VortexArena.Core.Combat
{
    /// <summary>
    /// Bir kavrama kaydı: elin <b>KUMANDA ANCHOR'ININ</b> (<c>OVRCameraRig.left/rightHandAnchor</c> —
    /// telde giden el pozunun ta kendisi) eşyaya göre yerel <b>KONUMU</b> + o elin parmak preset'i.
    /// Kavramanın tek yazılı kaynağı budur ve tanımın (<see cref="ItemDefinition"/>) içinde yaşar —
    /// prefabda kavrama düğümü YOKTUR.
    /// <para>
    /// ⚠️ <b>DÖNÜŞ YOKTUR ve eklenmez.</b> Eşyanın dönüşü her zaman ana kumandanın dönüşüdür
    /// (<see cref="ItemGripSolver"/>): kayıt yalnız "kumanda eşyanın NERESİNDE durur" der. Kayıt bir
    /// dönüş taşısaydı stüdyoda kökü çeviren (ya da yanlış eksende çizilen bir hayalet eli düzeltmeye
    /// çalışan) herkes oyunda silahı kumandadan saptırırdı — sahada belirtisi "el konumuna göre silah
    /// bozuk geliyor"dur ve teşhisi pahalıdır. Ön kabzada da aynı: ikinci elin kumandası eşyayla
    /// hizalı sayılır, sentetik bilek onun anchor→bilek deltası kadar ötesine kilitlenir.
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

        [Tooltip("Bu slotta elin parmak duruşu (Idle / Firing / Grip).")]
        public HandGripPreset preset;

        /// <summary>Bu kayıt yazıldı mı (yazılmamışsa alanları okunmaz).</summary>
        public bool IsAuthored => authored;

        /// <summary>
        /// Stüdyoda yazılmış bir konumdan kayıt üretir (<see cref="authored"/> = <c>true</c>).
        /// </summary>
        /// <param name="anchorInItem">Kumanda anchor'ının EŞYAYA göre yerel konumu — metre, ölçeksiz
        /// (bkz. sınıf uyarısı).</param>
        /// <param name="preset">O slotta elin parmak duruşu.</param>
        public static ItemGripPose From(in Vector3 anchorInItem, HandGripPreset preset)
        {
            return new ItemGripPose
            {
                authored = true,
                position = anchorInItem,
                preset = preset,
            };
        }
    }
}
