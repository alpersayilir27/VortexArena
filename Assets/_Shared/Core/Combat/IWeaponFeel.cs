using UnityEngine;

namespace VortexArena.Core.Combat
{
    /// <summary>MODE hook REPLACING the weapon's built-in recoil and fire haptic
    /// (<see cref="Weapon.FeelOverride"/>); <c>null</c> = the weapon's own kick and pulse.
    /// <para>⚠️ It is a REPLACEMENT, not an addition: while installed the weapon writes neither its
    /// linear pitch kick nor its fire vibration, so a hook that returns nothing leaves the model
    /// still and the controllers silent. Dry-fire, blocked-muzzle and reload cues stay with the
    /// weapon.</para>
    /// <para>⚠️ One installed instance serves EVERY weapon: per-weapon state must be keyed on the
    /// <see cref="Weapon"/> passed in and dropped in <see cref="OnReleased"/>.</para></summary>
    public interface IWeaponFeel
    {
        /// <summary>One trigger pull fired. Called at the END of the shot — after
        /// <see cref="Weapon.Fired"/>, the rays and the hit reports — exactly where the built-in kick
        /// and haptic pulse used to run.</summary>
        void OnShot(in WeaponShotContext shot);

        /// <summary>This frame's model offset, asked once per weapon in <c>Weapon.Update</c> (before
        /// the canonical grip in <c>LateUpdate</c>, so the kick tilts the muzzle for the next shot
        /// just like the built-in one).
        /// <para>Offsets are relative to the pivot's AUTHORED pose, in its parent space:
        /// <c>localRotation = baseRotation * rotation</c> and
        /// <c>localPosition = basePosition + baseRotation * position</c>. So
        /// <c>Vector3.zero</c> + <c>Quaternion.identity</c> means "at rest".</para></summary>
        /// <returns><c>false</c> = nothing to write this frame; the weapon leaves the pivot where it
        /// is instead of forcing the base pose.</returns>
        bool TryGetPivotOffset(Weapon weapon, float deltaTime, out Vector3 position, out Quaternion rotation);

        /// <summary>Drop this weapon's state: released, revoked, disabled, or the hook itself was
        /// swapped/removed. The pivot is returned to its base pose by the weapon right after.</summary>
        void OnReleased(Weapon weapon);
    }
}
