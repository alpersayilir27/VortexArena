using UnityEngine;
using UnityEngine.VFX;

namespace VortexArena.Core.Combat
{
    /// <summary>
    /// Fires a VFX Graph muzzle flash once per shot: subscribes to <see cref="Weapon.Fired"/> and
    /// sends the graph's spawn event.
    /// <para>⚠️ This bridge exists because <see cref="Weapon"/>'s own muzzle flash field is typed
    /// <c>ParticleSystem</c> — a <see cref="VisualEffect"/> cannot bind there. Without it the graph
    /// is driven only by its own initial event and free-runs instead of flashing per shot.</para>
    /// <para>⚠️ The graph's spawner must listen to <see cref="fireEventName"/> (NOT the implicit
    /// <c>OnPlay</c>) and run a single finite loop: one event = one burst, idle in between. A
    /// spawner left on the implicit event plays on enable and never stops.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public class WeaponMuzzleVfx : MonoBehaviour
    {
        [Tooltip("Boşsa bu nesnedeki VisualEffect kullanılır.")]
        [SerializeField] private VisualEffect effect;

        [Tooltip("Boşsa üst nesnelerde aranır.")]
        [SerializeField] private Weapon weapon;

        [Tooltip("Grafikteki spawner'ı tetikleyen olay adı.")]
        [SerializeField] private string fireEventName = "OnFire";

        private int fireEventId;
        private bool subscribed;

        private void Awake()
        {
            if (effect == null)
                effect = GetComponent<VisualEffect>();

            // Includes inactive parents: the weapon may be built while its holder is disabled.
            if (weapon == null)
                weapon = GetComponentInParent<Weapon>(true);

            fireEventId = Shader.PropertyToID(fireEventName);

            if (effect == null)
            {
                Debug.LogWarning($"[WeaponMuzzleVfx] {name}: VisualEffect bulunamadı — namlu alevi çalışmayacak.");
            }
            else if (weapon == null)
            {
                Debug.LogWarning($"[WeaponMuzzleVfx] {name}: Weapon bulunamadı — namlu alevi tetiklenmeyecek.");
            }
        }

        private void OnEnable()
        {
            if (weapon == null || effect == null || subscribed)
                return;

            weapon.Fired += HandleFired;
            subscribed = true;
        }

        private void OnDisable()
        {
            if (!subscribed)
                return;

            weapon.Fired -= HandleFired;
            subscribed = false;
        }

        private void HandleFired()
        {
            // Restarts the spawner's single loop; the previous burst's particles keep living out
            // their (few-frame) life, so sustained fire reads as one continuous flicker.
            effect.SendEvent(fireEventId);
        }
    }
}
