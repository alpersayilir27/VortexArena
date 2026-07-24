using UnityEngine;

/// <summary>
/// Plays weapon sound effects (fire / reload / dry-fire) through a 3D
/// AudioSource placed at the muzzle. The source is spatialized by the
/// Meta XR Audio plugin (project spatializer), so shots are localized
/// correctly in the arena for both the shooter and nearby players.
/// </summary>
public class WeaponAudio : MonoBehaviour
{
    [Tooltip("3D AudioSource at the muzzle (spatialize enabled).")]
    [SerializeField] private AudioSource source;
    [Tooltip("Fire clips; one is picked at random per shot.")]
    [SerializeField] private AudioClip[] fireClips;
    [SerializeField] private AudioClip reloadClip;
    [SerializeField] private AudioClip emptyClip;
    [Range(0f, 0.2f)]
    [Tooltip("Random pitch variation per shot so autofire does not sound robotic.")]
    [SerializeField] private float firePitchJitter = 0.05f;

    public void PlayFire()
    {
        if (source == null || fireClips == null || fireClips.Length == 0)
            return;
        source.pitch = 1f + Random.Range(-firePitchJitter, firePitchJitter);
        source.PlayOneShot(fireClips[Random.Range(0, fireClips.Length)]);
    }

    public void PlayReload()
    {
        if (source == null || reloadClip == null)
            return;
        source.pitch = 1f;
        source.PlayOneShot(reloadClip);
    }

    public void PlayEmpty()
    {
        if (source == null || emptyClip == null)
            return;
        source.pitch = 1f;
        source.PlayOneShot(emptyClip);
    }
}
