using UnityEngine;

namespace VortexArena.Core.Combat
{
    /// <summary>
    /// Silah seslerini namludaki 3D AudioSource üzerinden çalar. Kaynak, proje
    /// spatializer'ı (Meta XR Audio) ile konumlandırılır: atışlar hem atıcı hem
    /// yakındaki oyuncular için arenada doğru yerden duyulur.
    /// <para>
    /// Klip/pitch/volume değerleri <see cref="Configure"/> ile verilen
    /// <see cref="WeaponDefinition"/>'dan okunur; tanım yoksa (ya da ilgili klip alanı
    /// boşsa) Inspector'daki yedeklere düşülür. Şarjör sesleri (<see cref="PlayMagOut"/> /
    /// <see cref="PlayMagIn"/>) reload başlangıcında DEĞİL, WeaponAnimator'ın zaman
    /// çizgisinde çalınır — bu sınıf zamanlama tutmaz, yalnız çalar.
    /// </para>
    /// </summary>
    public class WeaponAudio : MonoBehaviour
    {
        [Tooltip("Namludaki 3D AudioSource (spatialize açık).")]
        [SerializeField] private AudioSource source;

        [Header("Yedekler (tanım atanmazsa)")]
        [Tooltip("Ateş klipleri; her atışta rastgele biri seçilir.")]
        [SerializeField] private AudioClip[] fireClips;
        [SerializeField] private AudioClip dryFireClip;
        [Range(0f, 0.2f)]
        [Tooltip("Atış başına rastgele pitch sapması (otomatik ateş robotik durmasın).")]
        [SerializeField] private float firePitchJitter = 0.05f;

        private WeaponDefinition definition;

        /// <summary>
        /// Ses değerlerinin kaynağını bağlar (<see cref="Weapon"/> Awake'te çağırır).
        /// null verilebilir — o zaman yalnız Inspector yedekleri kullanılır.
        /// </summary>
        public void Configure(WeaponDefinition definition)
        {
            this.definition = definition;
        }

        public void PlayFire()
        {
            AudioClip[] clips = definition != null && definition.FireClips != null && definition.FireClips.Length > 0
                ? definition.FireClips
                : fireClips;
            if (source == null || clips == null || clips.Length == 0)
                return;

            float pitchBase = definition != null ? definition.FirePitchBase : 1f;
            float jitter = definition != null ? definition.FirePitchJitter : firePitchJitter;
            float volume = definition != null ? definition.FireVolume : 1f;

            source.pitch = pitchBase + Random.Range(-jitter, jitter);
            source.PlayOneShot(clips[Random.Range(0, clips.Length)], volume);
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
            PlayClip(definition != null && definition.DryFireClip != null ? definition.DryFireClip : dryFireClip);
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
