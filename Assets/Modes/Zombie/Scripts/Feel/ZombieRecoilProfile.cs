using System;
using UnityEngine;

namespace VortexArena.Modes.Zombie
{
    /// <summary>One weapon's spring recoil amounts: the PEAK each channel reaches on a single shot.</summary>
    /// <remarks>⚠️ ONE-HAND baseline (the hold a pistol lives in). The grip multipliers in
    /// <see cref="ZombieFeel"/> scale it: ×0.55 two-handed, ×1.3 one-handed on a two-handed weapon.
    /// <para>⚠️ Peaks, not impulses: <see cref="ZombieWeaponFeel"/> derives the velocity kick that
    /// reaches exactly this peak for the configured damping, so retuning the spring does not silently
    /// rescale every weapon.</para></remarks>
    [Serializable]
    internal struct ZombieRecoilProfile
    {
        [Tooltip("Namluyu yukarı kaldıran tepe açı (derece).")]
        public float pitchDegrees;

        [Tooltip("Yana sapma tepe açısı (derece); her atışta rastgele ± yön.")]
        public float yawDegrees;

        [Tooltip("Dönme tepe açısı (derece); her atışta rastgele ± yön.")]
        public float rollDegrees;

        [Tooltip("Geri kayma tepe mesafesi (metre).")]
        public float backMeters;

        public ZombieRecoilProfile(float pitchDegrees, float yawDegrees, float rollDegrees, float backMeters)
        {
            this.pitchDegrees = pitchDegrees;
            this.yawDegrees = yawDegrees;
            this.rollDegrees = rollDegrees;
            this.backMeters = backMeters;
        }
    }

    /// <summary>Per-<c>weaponId</c> recoil override: wins over the weapon's class profile.</summary>
    [Serializable]
    internal struct ZombieRecoilOverride
    {
        [Tooltip("WeaponDefinition.weaponId — birebir aynı yazılır (ak47, nova …).")]
        public string weaponId;

        public ZombieRecoilProfile profile;
    }
}
