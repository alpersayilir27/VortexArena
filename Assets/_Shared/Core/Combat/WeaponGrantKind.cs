namespace VortexArena.Core.Combat
{
    /// <summary>
    /// HOW the weapon was granted — second argument of <see cref="Weapon.GrantTo"/>.
    /// <para>Why a separate type: one flag (<c>Weapon.IsGranted</c>) used to mean three things at
    /// once — "fixed in the hand (ISDK grab not run)", "reload CLOSED" and "single hand / no
    /// reserve". A frame-selected weapon wants only the first (its reload is open, it has a reserve
    /// and the second hand can hold the foregrip), so one flag would lock the three rules together
    /// and force an <c>if (modeId == …)</c> chain.</para>
    /// <para>⚠️ NOT serialized (runtime state only), so the "append at the END" rule is not binding
    /// here; the order is for readability.</para>
    /// </summary>
    public enum WeaponGrantKind
    {
        /// <summary>Not granted: the weapon is in the scene or is held via the ISDK grab.</summary>
        None,

        /// <summary>FFA's random weapon (§10.5 <c>weaponSource:"random"</c>): releasing grip
        /// DESTROYS it, pressing again yields a new one. Reload closed, no reserve, always
        /// one-handed.</summary>
        Disposable,

        /// <summary>Weapon selected from a frame: releasing grip only HIDES it, the same instance
        /// returns with the same ammo. Reload open, reserve present, the second hand can hold the
        /// foregrip.</summary>
        Persistent
    }
}
