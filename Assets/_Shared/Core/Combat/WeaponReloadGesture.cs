using UnityEngine;
using VortexArena.Core.Player;

namespace VortexArena.Core.Combat
{
    /// <summary>
    /// Below-the-waist reload gesture: pointing the held weapon down, lowering it below waist level
    /// and holding briefly calls <c>Weapon.TryStartReload</c>. Whether the reload actually starts
    /// (full magazine, no reserve, dead player…) is entirely TryStartReload's rule — this component
    /// only RECOGNIZES the gesture.
    /// <para>⚠️ The measured point is the HAND (controller anchor), NOT the weapon root: the root
    /// sits <c>gripPosition</c> forward along the barrel (0–34 cm per weapon) and that offset
    /// projects vertically with the weapon's angle — measuring the root would catch the same gesture
    /// at belly level on one weapon and knee level on another.</para>
    /// <para>⚠️ The measure is the hand's DROP BELOW THE EYE and the threshold is a ratio of
    /// STANDING eye height (<see cref="WaistDropRatio"/>) — exactly belt level on an upright player.
    /// Short and tall players lower their arm differently but both lower it "to the waist", which a
    /// fixed drop from the head (<c>headY − 0.62</c>) cannot follow. The ratio is then CLAMPED into a
    /// metre band (<see cref="MinWaistDropMeters"/>): the denominator is a MEASURE, and a measure that
    /// is wrong must not be able to move the waist line out of reach.</para>
    /// <para>⚠️ Both halves of the measure are deliberate and must not be mixed: the reference point
    /// is the LIVE eye (descends with the head), the scale is STANDING height
    /// (<see cref="StandingHeightState"/>, posture-independent). A floor-fixed line would put a
    /// crouching player's hanging arm below it and self-trigger the reload; scaling by live eye
    /// height would sink the threshold with the player and do the same. As written, crouching does
    /// NOT change the hand's drop below the eye.</para>
    /// <para>The second condition is the weapon pointing down, read from the CONTROLLER
    /// (<c>anchor.forward</c>): the weapon is already controller-aligned, so reading its own
    /// transform would learn the same thing through a per-model divergent path. ⚠️ The measure is in
    /// world space, i.e. independent of head rotation — looking down neither helps nor hinders.</para>
    /// <para>After a grab the gesture is not armed until the hand rises to chest level once —
    /// preventing false triggers when picking up from the ground/holster.</para>
    /// <para>A single haptic pulse confirms only when the reload ACTUALLY started. ⚠️ It buzzes
    /// because <c>TryStartReload</c> accepted, not because the gesture was recognized: buzzing on a
    /// rejected gesture would tell the player "reloaded" and the lie would surface only at the
    /// trigger. The one refusal that DOES answer — an empty reserve — is cued by <c>Weapon</c> itself
    /// with a different rhythm, because only <c>Weapon</c> knows which refusal it was.</para>
    /// <para>With no resolvable rig (admin spectator, editor session, unrecognized hand) the gesture
    /// is NEVER recognized: there is no correct answer to "where is the hand" in a hand-less
    /// session, so it stays silent instead of guessing.</para>
    /// </summary>
    public class WeaponReloadGesture : MonoBehaviour
    {
        /// <summary>
        /// Waist line: how far below the EYE the hand must drop, as a ratio of standing eye height.
        /// On an upright player this is exactly 62% of the floor-to-eye span (1 − 0.62):
        /// anthropometrically belt ≈ 60% of height, eye ≈ 93% → belt/eye ≈ 0.64. A smaller drop
        /// marks the belly, not the belt, and the gesture starts self-triggering.
        /// </summary>
        private const float WaistDropRatio = 0.38f;

        /// <summary>
        /// Metre band the waist line is clamped into after <see cref="WaistDropRatio"/> is applied.
        /// <para>⚠️ The denominator is a learned measure (<see cref="StandingHeightState"/>) and its two
        /// error directions are not symmetric: too small self-triggers the gesture on a hanging arm, too
        /// large puts the waist line where the arm cannot go and reload dies SILENTLY for the rest of the
        /// session. The band is the range the ratio yields across real players (eye 1.40–1.75 m →
        /// 0.53–0.67 m), so a sound measure never reaches it and a poisoned one cannot leave it.</para>
        /// </summary>
        private const float MinWaistDropMeters = 0.45f;

        /// <inheritdoc cref="MinWaistDropMeters"/>
        private const float MaxWaistDropMeters = 0.68f;

        /// <summary>
        /// Re-arm line as a fraction of the waist line: the hand must rise this far above the eye-drop
        /// that armed it. ⚠️ Expressed against the waist line, not against the raw height, so it inherits
        /// the clamp and cannot invert — two independent thresholds would let the re-arm band be
        /// forgotten whenever the waist line moves. 0.74 ≈ (0.38 − 0.10) / 0.38, i.e. a tenth of eye
        /// height above the belt.
        /// </summary>
        private const float RearmDropFactor = 0.74f;

        /// <summary>How far below horizontal the controller's forward axis must point: sin(25°).
        /// With the hand at the belt and a neutral wrist the muzzle sits ~20–30° down; a steeper
        /// threshold would tie the gesture to bending the wrist.</summary>
        private const float MinDownSin = 0.423f;

        /// <summary>How long the gesture must hold (seconds).</summary>
        private const float DwellSeconds = 0.15f;

        /// <summary>
        /// When the condition breaks the counter LEAKS instead of resetting; this is the leak rate
        /// relative to the dwell. ⚠️ Resetting would let one frame of tracking noise at the
        /// threshold kill the gesture; leaking absorbs it, while a player who really leaves the pose
        /// is cleared in <c>DwellSeconds / DwellDecayRate</c> seconds.
        /// </summary>
        private const float DwellDecayRate = 2f;

        [SerializeField] private Weapon weapon;

        private bool _armed;
        private float _dwell;

        private void Update()
        {
            if (weapon == null || !weapon.IsHeld)
            {
                Disarm();
                return;
            }

            // The HAND itself: the holding controller's anchor, not the weapon root.
            Transform anchor = WeaponGranter.ResolveHandAnchor(weapon.MainHand);
            if (anchor == null ||
                !WeaponGranter.TryResolveEyeAndFloor(out float eyeY, out float _) ||
                !StandingHeightState.TryGet(out float standingEye))
            {
                Disarm();
                return;
            }

            // Hand drop below the EYE: the reference descends with the head, the scale is standing
            // height, and the result is clamped to metres (rationale in MinWaistDropMeters).
            float drop = eyeY - anchor.position.y;
            float waistDrop = Mathf.Clamp(standingEye * WaistDropRatio,
                MinWaistDropMeters, MaxWaistDropMeters);

            if (!_armed)
            {
                // Armed only after the hand rises to chest level once.
                if (drop < waistDrop * RearmDropFactor)
                {
                    _armed = true;
                }

                return;
            }

            bool belowWaist = drop > waistDrop;
            bool pointingDown = -anchor.forward.y >= MinDownSin;

            if (belowWaist && pointingDown)
            {
                _dwell += Time.deltaTime;
                if (_dwell >= DwellSeconds)
                {
                    // Return value is ONLY for the haptic confirm: Weapon may reject (full mag, no
                    // reserve, dead player) and buzzing on rejection would say "done". The gesture
                    // resets either way; the player retries.
                    if (weapon.TryStartReload())
                    {
                        ControllerHaptics.PulseBoth(this, 1);
                    }

                    Disarm();
                }

                return;
            }

            // ⚠️ Leak, NOT reset (rationale in DwellDecayRate).
            _dwell = Mathf.Max(0f, _dwell - Time.deltaTime * DwellDecayRate);
        }

        private void Disarm()
        {
            _armed = false;
            _dwell = 0f;
        }
    }
}
