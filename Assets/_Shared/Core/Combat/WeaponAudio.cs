using UnityEngine;

namespace VortexArena.Core.Combat
{
    /// <summary>
    /// Silah seslerini namludaki 3D AudioSource üzerinden çalar. Kaynak, proje
    /// spatializer'ı (Meta XR Audio) ile konumlandırılır: atışlar hem atıcı hem
    /// yakındaki oyuncular için arenada doğru yerden duyulur.
    /// <para>
    /// ⚠️ <b>Klip/pitch/volume'ün TEK kaynağı <see cref="WeaponDefinition"/>'dır</b>
    /// (<c>WD_&lt;Ad&gt;.asset</c>, <see cref="Configure"/> ile bağlanır) — bu bileşende klip alanı
    /// YOKTUR ve eklenmez. Eskiden "tanım atanmazsa" diye Inspector yedekleri vardı; onlar
    /// ulaşılamaz koddu (<see cref="Weapon"/> tanımsız silahı hata basıp KİLİTLER, yani ateş hiç
    /// edilmez ve buraya sıra gelmez) ama Inspector'da ikinci bir "Fire Clips" listesi göstererek
    /// sesin hangi asset'ten geldiğini belirsizleştiriyordu. Ses değişikliği daima
    /// <c>WD_&lt;Ad&gt;.asset</c>'te yapılır.
    /// </para>
    /// <para>
    /// Şarjör sesleri (<see cref="PlayMagOut"/> / <see cref="PlayMagIn"/>) reload başlangıcında
    /// DEĞİL, WeaponAnimator'ın zaman çizgisinde çalınır — bu sınıf zamanlama tutmaz, yalnız çalar.
    /// </para>
    /// </summary>
    public class WeaponAudio : MonoBehaviour
    {
        [Tooltip("Namludaki 3D AudioSource (spatialize açık).")]
        [SerializeField] private AudioSource source;

        private WeaponDefinition definition;

        /// <summary>
        /// Ses değerlerinin kaynağını bağlar (<see cref="Weapon"/> Awake'te çağırır).
        /// null verilebilir ve o zaman bu bileşen TÜMÜYLE sessizdir — tanımsız silah zaten
        /// <see cref="Weapon"/> tarafından kilitleniyor, yani bu durum bir arıza göstergesidir.
        /// </summary>
        public void Configure(WeaponDefinition definition)
        {
            this.definition = definition;
        }

        public void PlayFire()
        {
            if (source == null || definition == null)
                return;

            AudioClip[] clips = definition.FireClips;
            if (clips == null || clips.Length == 0)
                return;

            // ⚠️ Seçilen eleman NULL olabilir: dizinin boyu doldurulmuş ama bir yuvası boş
            // bırakılmış olabilir (Inspector'da sık). O atış sessiz geçer — dizi boyuna bakıp
            // "klip var" saymak, yarısı boş bir listede atışların yarısını sessizleştirirdi.
            AudioClip clip = clips[Random.Range(0, clips.Length)];
            if (clip == null)
                return;

            source.pitch = definition.FirePitchBase +
                           Random.Range(-definition.FirePitchJitter, definition.FirePitchJitter);
            source.PlayOneShot(clip, definition.FireVolume);
        }

        public void PlayMagOut()
        {
            PlayClip(definition != null ? definition.MagOutClip : null);
        }

        public void PlayMagIn()
        {
            PlayClip(definition != null ? definition.MagInClip : null);
        }

        public void PlayDry()
        {
            PlayClip(definition != null ? definition.DryFireClip : null);
        }

        public void PlayPickup()
        {
            PlayClip(definition != null ? definition.PickupClip : null);
        }

        /// <summary>Klip null ise sessiz no-op (her silahın her sesi olmak zorunda değil).</summary>
        private void PlayClip(AudioClip clip)
        {
            if (source == null || clip == null)
                return;

            source.pitch = 1f;
            source.PlayOneShot(clip);
        }
    }
}
