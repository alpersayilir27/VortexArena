namespace VortexArena.Modes.Zombie
{
    /// <summary>Feel class of a weapon: which recoil profile, fire haptic and close-fire layer it gets.</summary>
    /// <remarks>⚠️ Not a balance concept — <see cref="ZombieArsenal"/> owns damage and unlock waves.
    /// The map from <c>weaponId</c> lives in <see cref="ZombieWeaponFeel"/>; <c>WeaponDefinition</c>
    /// carries no class field, so an id missing from the map falls back to <see cref="Rifle"/>.</remarks>
    internal enum ZombieWeaponClass
    {
        Pistol = 0,
        Magnum = 1,
        Smg = 2,
        Rifle = 3,
        Shotgun = 4,
    }
}
