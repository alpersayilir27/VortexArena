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
    /// iskeletin kendi bind pozundan ölçülür (Mixamo eli için <c>HandFingerRig</c>, ISDK eli için
    /// <see cref="HandGripPresets"/>), oran yalnız "ne kadar" der.
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

        // ⚠️ Aşağıdaki üç duruş, üç parmak preset'inin (HandGripPreset) SAYILARIDIR ve projedeki
        // TEK yazılı kaynaklarıdır: yerel sentetik el, stüdyodaki hayalet el ve uzak avatarın
        // Mixamo eli üçü de bunlardan sürülür (HandGripPresets). Başka hiçbir yerde tekrar
        // yazılmaz — ikinci bir kopya, stüdyoda görülen el ile oyunda görülen elin sessizce
        // ayrışması demektir.

        /// <summary>
        /// <see cref="HandGripPreset.Idle"/> — boşta duran elin gevşek duruşu: parmaklar serçeye
        /// doğru artan biçimde hafif kıvrık (anatomik dinlenme duruşu; tümü sıfır olsaydı el tahta
        /// gibi düz dururdu).
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
        /// <see cref="HandGripPreset.Firing"/> — tetiği olan elin duruşu: işaret parmağı tetikte
        /// (az kapalı), diğer üçü kabzayı sarar, başparmak üstte.
        /// </summary>
        public static HandPoseProfile Firing => new HandPoseProfile
        {
            thumb = 0.50f,
            index = 0.35f,
            middle = 0.85f,
            ring = 0.90f,
            pinky = 0.90f,
        };

        /// <summary>
        /// <see cref="HandGripPreset.Grip"/> — saran duruş: beş parmak da kapanır (ön kabza, tetiği
        /// olmayan el). İşaret parmağı burada <see cref="Firing"/>'den ayrılır: sarmanın tetikle
        /// işi yoktur.
        /// </summary>
        public static HandPoseProfile Grip => new HandPoseProfile
        {
            thumb = 0.60f,
            index = 0.85f,
            middle = 0.90f,
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
