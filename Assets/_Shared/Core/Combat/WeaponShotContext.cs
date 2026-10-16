using UnityEngine;

namespace VortexArena.Core.Combat
{
    /// <summary>Everything one trigger pull tells <see cref="IWeaponFeel"/>: which weapon fired, how
    /// it was held and which controllers to drive.
    /// <para>⚠️ Read-only snapshot taken at the shot — the grip may change before the hook's next
    /// frame, so a hook keeping it must not treat <see cref="MainHand"/> as still valid.</para></summary>
    public readonly struct WeaponShotContext
    {
        public readonly Weapon Weapon;

        /// <summary>Balance/feel source of the weapon; never <c>null</c> on this path (a weapon with
        /// no definition cannot fire).</summary>
        public readonly WeaponDefinition Definition;

        /// <summary>Held with two hands (<see cref="Weapon.IsTwoHanded"/>) at the moment of the shot.</summary>
        public readonly bool TwoHanded;

        /// <summary>Trigger hand; <c>None</c> = unresolved controller (Editor session).</summary>
        public readonly OVRInput.Controller MainHand;

        /// <summary>Front-grip hand; <c>None</c> = none or unresolved.</summary>
        public readonly OVRInput.Controller SupportHand;

        /// <summary>Grip recoil scale of this shot (<see cref="Weapon.GripRecoilScale"/>): the
        /// two-hand reference multiplier with the weapon's own one-hand penalty already folded in, so
        /// the hook does not re-derive the grip rule.</summary>
        public readonly float RecoilScale;

        public WeaponShotContext(Weapon weapon, WeaponDefinition definition, bool twoHanded,
            OVRInput.Controller mainHand, OVRInput.Controller supportHand, float recoilScale)
        {
            Weapon = weapon;
            Definition = definition;
            TwoHanded = twoHanded;
            MainHand = mainHand;
            SupportHand = supportHand;
            RecoilScale = recoilScale;
        }
    }
}
