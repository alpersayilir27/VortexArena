using TMPro;
using UnityEngine;
using VortexArena.Core.Combat;

namespace VortexArena.Modes.Zombie
{
    /// <summary>Writes "N. DALGA" into a zombie board module's header from the wave table.</summary>
    /// <remarks>The wave is read from the first weapon under the module, so the board holds no second
    /// copy of <see cref="ZombieArsenal"/>; moving a weapon to another module retitles it.</remarks>
    public sealed class ZombieWaveModuleLabel : MonoBehaviour
    {
        [SerializeField] private TMP_Text title;

        private void Start()
        {
            if (title == null)
            {
                return;
            }

            Weapon weapon = GetComponentInChildren<Weapon>(true);
            if (weapon == null || weapon.Definition == null)
            {
                return;
            }

            title.text = ZombieArsenal.Live.UnlockWave(weapon.Definition.WeaponId) + ". DALGA";
        }
    }
}
