using UnityEngine;

namespace VortexArena.Core.Combat
{
    /// <summary>
    /// Math of the canonical grip — the ONE solver running on both the local and remote end.
    /// <para>Why a separate, pure class: both sides must compute the same pose (it does not travel
    /// on the wire, §6.6). Two implementations would mean fixing one and forgetting the other, so
    /// the same weapon would look different on each screen. No scene/component dependency here:
    /// input is two palm poses + the definition, output is the item's world pose.</para>
    /// <para>One hand: the item's rotation is the main controller's, its position is the grip record
    /// carried by that rotation. Two hands: start from the one-hand solution and turn the item's
    /// <i>main grip → foregrip</i> axis toward the second palm. The second hand does not MOVE the
    /// item, it only AIMS it — the main grip point stays exactly on the main palm every frame.</para>
    /// <para>⚠️ HAND ROTATION NEVER ENTERS. The grip record carries POSITION only
    /// (<see cref="ItemGripPose"/>) and two-handed aiming reads only the second hand's POSITION —
    /// neither the second controller's rotation nor the wrist pose. Roll always comes from the main
    /// controller (<see cref="Quaternion.FromToRotation"/> takes the shortest arc and produces no
    /// roll of its own). An axis tied to the record or the second hand's rotation would skew the
    /// weapon by how the player twists their hand, seen in the field as "the weapon comes out
    /// wrong".</para>
    /// <para>⚠️ Smoothing does not belong here (<see cref="StepAimBlend"/> is separate): while the
    /// solver stays pure the same function can run on both ends. Locally the blend is driven by a
    /// time constant; remotely the wire's own interpolation already smooths it.</para>
    /// </summary>
    public static class ItemGripSolver
    {
        /// <summary>
        /// Upper bound of the two-handed aim's FULL-FOLLOW band (degrees): up to here the weapon
        /// tracks the second hand exactly.
        /// <para>⚠️ Nothing to do with GRABBING: the foregrip link only watches the grip button
        /// (<c>WeaponGranter.ResolveSecondaryHand</c>); these two constants say how far the weapon
        /// TURNS.</para>
        /// </summary>
        private const float AimFullAngleDegrees = 120f;

        /// <summary>
        /// Angle (degrees) at which the two-handed aim is dropped entirely: beyond this the second
        /// hand is ignored and the weapon stays in the main hand's pose.
        /// <see cref="ReachWeight"/> fades between the two.
        /// <para>⚠️ A hard clamp is NOT put back here. A clamp only limits the magnitude of the
        /// rotation, but the real problem is that <see cref="Quaternion.FromToRotation"/> becomes
        /// UNDEFINED as the two vectors approach anti-parallel: the axis is <c>from × to</c>, and as
        /// that product goes to zero its DIRECTION flips on the slightest noise — the weapon snaps
        /// to the opposite side. A clamp does not see this, it only trims the snap.</para>
        /// <para>The band solves it by ZEROING THE WEIGHT: on reaching the singularity the applied
        /// rotation's weight is already 0, so axis noise there has no visual effect. The value sits
        /// beyond any pose physically holdable with two hands — inside the band the current feel is
        /// untouched, outside it fades smoothly to the one-handed pose.</para>
        /// </summary>
        private const float AimFadeOutAngleDegrees = 160f;

        /// <summary>Below this foregrip axis length (1 cm) the direction is undefined: the secondary
        /// socket was never authored, so the two-hand solution does not run.</summary>
        private const float MinAxisSqr = 1e-4f;

        /// <summary>Below this palm separation (5 cm) the target direction is noise — with the hands
        /// on top of each other the weapon would snap around on tiny jitters.</summary>
        private const float MinReachSqr = 0.0025f;

        /// <summary>Time constant of the 0↔1 transition as the second hand grabs/releases (seconds).</summary>
        private const float AimBlendSeconds = 0.08f;

        /// <summary>
        /// Resolves the item's world pose.
        /// </summary>
        /// <param name="def">Item definition (source of the grip record); <c>null</c> sticks the
        /// item to the palm.</param>
        /// <param name="primaryRight">Is the main hand RIGHT — required, the record is per hand.</param>
        /// <param name="secondaryRight">Is the foregrip hand RIGHT. With no second hand yet, pass
        /// the opposite of the main hand: the value is unused then, but both ends produce the same
        /// result.</param>
        /// <param name="primaryPalm">Main hand PALM pose (<c>HandGripPivot.Resolve</c> output = the
        /// controller anchor).</param>
        /// <param name="hasSecondary">Is the second hand on the foregrip.</param>
        /// <param name="secondaryPalmPosition">Second hand's palm POSITION. ⚠️ Its rotation is
        /// deliberately not requested: aiming comes from where the hand reaches, not how it is
        /// twisted.</param>
        /// <param name="aimBlend">0..1 — the caller's smoothing; at 0 the result is the one-handed
        /// solution.</param>
        public static void Solve(ItemDefinition def, bool primaryRight, bool secondaryRight,
                                 in Pose primaryPalm, bool hasSecondary,
                                 in Vector3 secondaryPalmPosition, float aimBlend,
                                 out Vector3 itemPosition, out Quaternion itemRotation)
        {
            // The one-handed solution is ALWAYS computed and returned as-is when the two-handed
            // branch's guards fail — so "the two-hand solution did not run" has a defined result.
            // ⚠️ The base rotation IS the main controller's rotation: the record carries no rotation,
            // so there is nothing to multiply in (unauthored record → same result, zero position).
            itemRotation = primaryPalm.rotation;

            // A definition-less item has no grip offset either: sticking it to the palm beats
            // dropping it at the world origin (Weapon.Awake already logs the missing definition).
            if (def == null)
            {
                itemPosition = primaryPalm.position;
                return;
            }

            // The record is the controller's local position on the item; the item is shifted back so
            // the controller lands on that point (PrimaryGripPosition carries the minus sign).
            Vector3 gripPointOnItem = def.PrimaryGripPointOnItem(primaryRight);
            itemPosition = primaryPalm.position + itemRotation * def.PrimaryGripPosition(primaryRight);

            float blend = Mathf.Clamp01(aimBlend);
            // An unauthored foregrip falls on the item root (ItemDefinition.HasSecondaryGrip);
            // solving with that axis would "aim" the weapon at the main hand — stay one-handed.
            if (!def.HasSecondaryGrip || !hasSecondary || blend <= 0f)
            {
                return;
            }

            Vector3 axisLocal = def.SecondaryGripPosition(secondaryRight) - gripPointOnItem;
            Vector3 to = secondaryPalmPosition - primaryPalm.position;
            if (axisLocal.sqrMagnitude < MinAxisSqr || to.sqrMagnitude < MinReachSqr)
            {
                return;
            }

            Vector3 from = itemRotation * axisLocal;

            // ⚠️ The angle is measured with Vector3.Angle, NOT Quaternion.ToAngleAxis: due to double
            // cover (q and −q are the same rotation) the latter can return an angle EXCEEDING 180°
            // and a SIGN-FLIPPED axis near anti-parallel, and any rotation on that axis throws the
            // weapon the opposite way. Vector3.Angle is always in [0,180] and carries no such
            // ambiguity.
            float reach = ReachWeight(Vector3.Angle(from, to));
            if (reach <= 0f)
            {
                // Target out of arm's reach. Not a BREAK of the link (that watches the grip button)
                // and no jump: the weight already faded to zero inside the band.
                return;
            }

            Quaternion full = Quaternion.FromToRotation(from, to);
            Quaternion delta = Quaternion.Slerp(Quaternion.identity, full, blend * reach);

            itemRotation = delta * primaryPalm.rotation;

            // ⚠️ Position is rebuilt AFTER the rotation and in reverse, so the main grip point stays
            // exactly on the main palm: the two-handed solution changes ORIENTATION only, it does
            // not SLIDE the weapon toward the second hand (which would look like the main hand let
            // go). Identity check: with delta = identity this line is IDENTICAL to the one-handed
            // position above — PrimaryGripPosition is by definition −PrimaryGripPointOnItem.
            itemPosition = primaryPalm.position - itemRotation * gripPointOnItem;
        }

        /// <summary>
        /// Reachability weight of the target direction: 1 = the second hand is followed exactly,
        /// 0 = it is ignored (weapon stays in the main hand's pose), <c>SmoothStep</c> between.
        /// <para>Why a weight and not a clamp: rationale in <see cref="AimFadeOutAngleDegrees"/> —
        /// a clamp trims the snap's magnitude, the weight makes the snap invisible, and being
        /// continuous the weapon does not jump at either edge of the band.</para>
        /// <para>⚠️ The SAME function runs locally and remotely (that is why the solver is pure):
        /// computed in the caller, the same weapon would look straight on one screen and skewed on
        /// the other.</para>
        /// </summary>
        public static float ReachWeight(float angleDegrees)
        {
            if (angleDegrees <= AimFullAngleDegrees)
            {
                return 1f;
            }

            if (angleDegrees >= AimFadeOutAngleDegrees)
            {
                return 0f;
            }

            float t = (angleDegrees - AimFullAngleDegrees) /
                      (AimFadeOutAngleDegrees - AimFullAngleDegrees);
            return Mathf.SmoothStep(1f, 0f, t);
        }

        /// <summary>
        /// Steps the two-handed solution's weight one tick toward its target.
        /// <para>State lives in the caller so the solver stays pure and the same function can run on
        /// both ends. Remotely the wire's interpolation already smooths it and this is never called.</para>
        /// </summary>
        public static float StepAimBlend(float current, bool wantTwoHand, float deltaTime)
        {
            return Mathf.MoveTowards(current, wantTwoHand ? 1f : 0f, deltaTime / AimBlendSeconds);
        }
    }
}
