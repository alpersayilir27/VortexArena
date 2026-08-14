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
    /// ⚠️ <b><see cref="euler"/> eşyayı DÖNDÜRMEZ.</b> Silahın eldeki dönüşü kimliktir (eşyanın
    /// eksenleri kumanda anchor'ıyla birebir aynıdır, bkz.
    /// <see cref="ItemDefinition.PrimaryGripRotation"/>); buradaki dönüş yalnız EL MODELİNİN bilek
    /// dönüşüdür (<c>HandGripPoser</c> sentetik elin bileğini ona kilitler). Yakalanan dönüşü
    /// eşyaya da uygulamak "kumandayı uzatınca namlu başka yöne bakıyor" demekti.
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

        [Tooltip("Elin (ISDK bileğinin) EŞYAYA göre yerel dönüşü (derece, Euler). Yalnız el " +
                 "modelini sürer; eşyayı döndürmez.")]
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
