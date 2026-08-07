using System;
using UnityEngine;

namespace VortexArena.Core.Combat
{
    /// <summary>
    /// Bir elin parmak duruşu — <b>beş sayı</b>, parmak başına kapanma miktarı (<c>0</c> = açık,
    /// <c>1</c> = tam kapalı).
    /// <para>
    /// ⚠️ <b>Neden quaternion değil de kapanma oranı:</b> duruş iki ayrı iskelete uygulanabilmeli
    /// (karakterin humanoid eli ve ileride başka bir model) ve iki iskeletin kemik eksenleri aynı
    /// değil. Ham rotasyon taşımak, projenin zaten bir kez öğrendiği tuzağın parmak ölçeğinde
    /// tekrarı olurdu (<c>Docs/Sistem-Ozeti.md</c> §7, "izleme/ağ uzayından gelen rotasyon humanoid
    /// kemiğe doğrudan yazılmaz"). Oran ise rig'e bağlı değildir: eksen çalışma anında <b>o</b>
    /// iskeletin kendi bind pozundan ölçülür (<c>HandFingerRig</c>), oran yalnız "ne kadar"
    /// der.
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

        /// <summary>
        /// Boşta duran elin gevşek duruşu — parmaklar serçeye doğru artan biçimde hafif kıvrık
        /// (anatomik dinlenme duruşu; tümü sıfır olsaydı el tahta gibi düz dururdu).
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
        /// Eşyası duruş yazmamışsa kullanılan kavrama: işaret parmağı tetikte (az kapalı), diğer
        /// üçü kabzayı sarar, başparmak üstte.
        /// <para>Bu bir <b>başlangıç değeridir</b>, hedef değil — her silahın kendi duruşu
        /// <c>ItemDefinition</c>'da yazılır.</para>
        /// </summary>
        public static HandPoseProfile DefaultGrip => new HandPoseProfile
        {
            thumb = 0.50f,
            index = 0.35f,
            middle = 0.85f,
            ring = 0.90f,
            pinky = 0.90f,
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
    }
}
