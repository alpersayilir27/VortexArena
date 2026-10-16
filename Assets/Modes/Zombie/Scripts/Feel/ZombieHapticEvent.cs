namespace VortexArena.Modes.Zombie
{
    /// <summary>Haptic cues the zombie mode owns; index into <see cref="ZombieHaptics"/>' player cache.</summary>
    internal enum ZombieHapticEvent
    {
        FirePistol = 0,
        FireMagnum = 1,
        FireSmg = 2,
        FireRifle = 3,
        FireShotgun = 4,
        HitConfirm = 5,
        HitHead = 6,
        Kill = 7,
        HeadPop = 8,
        Dismember = 9,
        PlayerHurt = 10,

        /// <summary>Count marker — keep LAST.</summary>
        Count = 11,
    }
}
