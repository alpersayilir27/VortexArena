using System;
using UnityEngine;

namespace VortexArena.Core.Combat
{
    /// <summary>
    /// Bir elin parmak duruşunun <b>kaba</b> hâli — beş sayı, parmak başına kapanma miktarı
    /// (<c>0</c> = açık, <c>1</c> = tam kapalı). <b>Uzak avatarın humanoid (Mixamo) eli</b> ve boş
    /// el bununla sürülür.
    /// <para>
    /// ⚠️ <b>Neden humanoid ele quaternion değil de kapanma oranı gider:</b> silaha özel riglenen
    /// duruş ISDK iskeletinin eklem dönüşleridir (<see cref="HandJointRotation"/>) ve iki iskeletin
    /// kemik eksenleri aynı değildir. Ham rotasyon taşımak, projenin zaten bir kez öğrendiği tuzağın
    /// parmak ölçeğinde tekrarı olurdu (<c>Docs/Sistem-Ozeti.md</c> §7, "izleme/ağ uzayından gelen
    /// rotasyon humanoid kemiğe doğrudan yazılmaz"). Oran ise rig'e bağlı değildir: eksen çalışma
    /// anında <b>o</b> iskeletin kendi bind pozundan ölçülür (Mixamo eli için
    /// <c>HandFingerRig</c>), oran yalnız "ne kadar" der. Riglenmiş duruştan oranı çıkaran
    /// tek yer <see cref="HandPoseLibrary.MeasureCurl"/>'dür ve sonucu <b>asset'te saklanmaz</b> —
    /// ikinci bir doğruluk kaynağı doğmasın.
    /// </para>
    /// <para>
    /// ⚠️ <b>Telde GİTMEZ</b> (§6.9): parmakların nerede duracağı bir ölçüm değil bir kavrama
    /// sorusudur ve cevabı her istemcinin APK'sında. Aynı silah bu yüzden her ekranda aynı tutulur
    /// ve sol/sağ farkı oluşmaz — duruşun kaynağı ele değil <b>eşyaya</b> bağlı.
    /// </para>
    /// </summary>
    [Serializable]
    public struct HandPoseProfile
    {
        [Range(0f, 1f)] public float thumb;
        [Range(0f, 1f)] public float index;
        [Range(0f, 1f)] public float middle;
        [Range(0f, 1f)] public float ring;
        [Range(0f, 1f)] public float pinky;

        // ⚠️ Burada YALNIZ boş elin duruşu durur. Kavrama duruşları buraya geri EKLENMEZ: silahın
        // elde nasıl tutulacağı silah başına riglenir (ItemGripPose.fingerJoints) ve tek yazılı
        // kaynağı WD_*.asset'tir. Ortak bir "sıkma/kabza" tablosu, riglenmiş silahla riglenmemiş
        // silahın aynı görünmesi demekti — aracın çözdüğü sorunun ta kendisi.

        /// <summary>
        /// Boşta duran elin gevşek duruşu: parmaklar serçeye doğru artan biçimde hafif kıvrık
        /// (anatomik dinlenme duruşu; tümü sıfır olsaydı el tahta gibi düz dururdu).
        /// <para>Eşya bırakılınca el buna döner; kavraması riglenmemiş bir slot da buraya düşer.</para>
        /// </summary>
        public static HandPoseProfile Idle => new HandPoseProfile
        {
            thumb = 0.15f,
            index = 0.25f,
            middle = 0.30f,
            ring = 0.35f,
            pinky = 0.40f,
        };

        /// <summary>
        /// Hiç yazılmamış (tümü sıfır) mı. ⚠️ Bu kapı <b>gerekli</b>: alanı olmayan eski
        /// <c>WD_*.asset</c>'ler deserialize edilince beş sıfır okunur ve o "tahta el" demektir.
        /// Sıfırın "tam açık el" olarak meşru bir kullanımı yok, o yüzden yazılmamışla eş sayılır.
        /// </summary>
        public bool IsEmpty =>
            thumb <= 0f && index <= 0f && middle <= 0f && ring <= 0f && pinky <= 0f;

        /// <summary>Parmak sırasına göre kapanma oranı (0=başparmak … 4=serçe).</summary>
        public float Get(int fingerIndex)
        {
            switch (fingerIndex)
            {
                case 0: return thumb;
                case 1: return index;
                case 2: return middle;
                case 3: return ring;
                default: return pinky;
            }
        }

        /// <summary>
        /// İki duruş arasında parmak parmak doğrusal karışım — duruş geçişinin (boş el ↔ kavrama)
        /// uzak avatardaki adımı (<c>RemoteHandPoser</c>); süre ve eğri
        /// <see cref="HandPoseLibrary.TransitionSeconds"/> / <see cref="HandPoseLibrary.Ease"/>'ten
        /// gelir, burada yalnız karıştırılır.
        /// </summary>
        public static HandPoseProfile Lerp(in HandPoseProfile a, in HandPoseProfile b, float t)
        {
            t = Mathf.Clamp01(t);
            return new HandPoseProfile
            {
                thumb = Mathf.Lerp(a.thumb, b.thumb, t),
                index = Mathf.Lerp(a.index, b.index, t),
                middle = Mathf.Lerp(a.middle, b.middle, t),
                ring = Mathf.Lerp(a.ring, b.ring, t),
                pinky = Mathf.Lerp(a.pinky, b.pinky, t),
            };
        }

        /// <summary>Beş oran da eşit mi (geçiş hedefinin değişip değişmediğini anlamak için).</summary>
        public bool Approximately(in HandPoseProfile other)
        {
            return Mathf.Approximately(thumb, other.thumb) &&
                   Mathf.Approximately(index, other.index) &&
                   Mathf.Approximately(middle, other.middle) &&
                   Mathf.Approximately(ring, other.ring) &&
                   Mathf.Approximately(pinky, other.pinky);
        }
    }
}
