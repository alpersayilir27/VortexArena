using UnityEngine;

namespace VortexArena.Core.Combat
{
    /// <summary>
    /// Plays weapon sounds through the 3D AudioSource at the muzzle, spatialized by the project
    /// spatializer (Meta XR Audio) so shots come from the right place for shooter and bystanders.
    /// <para>⚠️ The SINGLE source of clip/pitch/volume is <see cref="WeaponDefinition"/>
    /// (<c>WD_&lt;Name&gt;.asset</c>, bound via <see cref="Configure"/>) — this component has NO
    /// clip field and none is added. The old "if no definition" Inspector fallbacks were
    /// unreachable (<see cref="Weapon"/> LOCKS a weapon without a definition) yet a second "Fire
    /// Clips" list blurred which asset the sound came from.</para>
    /// <para>Magazine sounds (<see cref="PlayMagOut"/> / <see cref="PlayMagIn"/>) are played on
    /// WeaponAnimator's timeline, not at reload start — this class keeps no timing.</para>
    /// </summary>
    public class WeaponAudio : MonoBehaviour
    {
        [Tooltip("Namludaki 3D AudioSource (spatialize açık).")]
        [SerializeField] private AudioSource source;

        private WeaponDefinition definition;

        /// <summary>
        /// Binds the source of the audio values (called by <see cref="Weapon"/> in Awake).
        /// null leaves this component COMPLETELY silent — a definition-less weapon is already
        /// locked by <see cref="Weapon"/>, so that state is a fault indicator.
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

            // ⚠️ The picked element may be NULL (array sized but a slot left empty — common in the
            // Inspector). That shot goes silent; inferring "there is a clip" from the array length
            // would mute half the shots on a half-empty list.
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

        /// <summary>Silent no-op when the clip is null (not every weapon must have every sound).</summary>
        private void PlayClip(AudioClip clip)
        {
            if (source == null || clip == null)
                return;

            source.pitch = 1f;
            source.PlayOneShot(clip);
        }
    }
}
