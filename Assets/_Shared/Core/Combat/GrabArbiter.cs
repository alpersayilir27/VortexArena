using UnityEngine;
using Object = UnityEngine.Object;

namespace VortexArena.Core.Combat
{
    /// <summary>
    /// Picks ONE winner per hand out of the proximity grip presses offered in a frame.
    /// <para>⚠️ <b>Why this cannot live in the socket.</b> Every claimant reads the raw grip axis in its
    /// own <c>LateUpdate</c> and asks only its OWN <see cref="GripSocket"/>, which knows nothing about
    /// the others. Wherever sockets overlap — ingredients stacked on a board, a dispenser standing among
    /// what it hands out — a single press was answered by several claimants at once: several objects
    /// into one palm, several messages on the wire, several writers on one hand. A component that sees
    /// one socket cannot decide this; only something that sees every candidate can.</para>
    /// <para><b>Nearest wins</b>, measured with <see cref="GripSocket.TryMeasure"/> — the same number
    /// behind the take gate and the indicator, so what the player reached for is what arrives. A tie
    /// keeps the earlier claim: a winner that flips between frames reads as a dropped press.</para>
    /// <para><b>Preview lane</b> (<see cref="SubmitPreview"/>): the same ranking with no press, so the
    /// highlight the player sees is decided by the code that will answer the press — a second "what
    /// would I grab" measure eventually disagrees with the grab itself.</para>
    /// <para>⚠️ This gates the PRESS path only. A carrier claiming its whole cargo
    /// (<c>BurgerCarrier.Claim</c>) is a deliberate multi-grab on another path and must not be routed
    /// through here, or a tray would come up carrying one ingredient.</para>
    /// </summary>
    public static class GrabArbiter
    {
        private struct Claim
        {
            public IGrabClaimant Claimant;
            public float Distance;
            public int Frame;
        }

        private static Claim _left;
        private static Claim _right;

        // Preview lane: the same ranking with NO press, so the highlight can name exactly what a press
        // would take. Kept apart from the press lane — a preview must never grab anything.
        private static Claim _previewLeft;
        private static Claim _previewRight;
        private static IGrabClaimant _previewWinnerLeft;
        private static IGrabClaimant _previewWinnerRight;

        /// <summary>Offers a claimant as this frame's candidate for that hand.</summary>
        /// <param name="distance">Controller anchor to socket (m) — the ranking key.</param>
        public static void Submit(IGrabClaimant claimant, bool rightHand, float distance)
        {
            if (!IsAlive(claimant))
            {
                return;
            }

            if (rightHand)
            {
                Offer(ref _right, claimant, distance);
            }
            else
            {
                Offer(ref _left, claimant, distance);
            }
        }

        /// <summary>Offers a claimant as this frame's HIGHLIGHT candidate for that hand — the same
        /// ranking, without a press and without any grab.</summary>
        /// <param name="distance">Controller anchor to socket (m) — the ranking key.</param>
        public static void SubmitPreview(IGrabClaimant claimant, bool rightHand, float distance)
        {
            if (!IsAlive(claimant))
            {
                return;
            }

            if (rightHand)
            {
                Offer(ref _previewRight, claimant, distance);
            }
            else
            {
                Offer(ref _previewLeft, claimant, distance);
            }
        }

        /// <summary>Would this claimant win a hand right now (the highlight gate).</summary>
        /// <remarks>⚠️ Answers the LAST resolved frame: claimants offer at the default order and the
        /// resolve runs at 40, so a same-frame answer would only ever be "the first one that asked".</remarks>
        public static bool IsPreviewWinner(IGrabClaimant claimant)
        {
            return claimant != null &&
                   (ReferenceEquals(_previewWinnerLeft, claimant) ||
                    ReferenceEquals(_previewWinnerRight, claimant));
        }

        /// <summary>Hands this frame's nearest claim to its claimant and clears both hands. Driven by
        /// <see cref="GrabArbiterPump"/>, which runs after every claimant has offered.</summary>
        public static void Resolve()
        {
            Take(ref _left, false);
            Take(ref _right, true);

            _previewWinnerLeft = Winner(ref _previewLeft);
            _previewWinnerRight = Winner(ref _previewRight);
        }

        /// <summary>Drops every claim. Statics outlive a play session while domain reload is off.</summary>
        public static void Reset()
        {
            _left = default;
            _right = default;
            _previewLeft = default;
            _previewRight = default;
            _previewWinnerLeft = null;
            _previewWinnerRight = null;
        }

        /// <summary>This frame's nearest preview claimant, consuming the claim — a hand that stopped
        /// offering (grabbed, or moved away) has no winner at all.</summary>
        private static IGrabClaimant Winner(ref Claim claim)
        {
            IGrabClaimant claimant = claim.Claimant;
            int frame = claim.Frame;
            claim = default;

            return frame == Time.frameCount && IsAlive(claimant) ? claimant : null;
        }

        private static void Offer(ref Claim claim, IGrabClaimant claimant, float distance)
        {
            // ⚠️ A claim from an earlier frame is stale: overwritten, never compared. Otherwise a resolve
            // pass that failed to run would leave a dead claimant holding the hand for the rest of the
            // match, and the symptom would be "the grab button stopped working" with nothing logged.
            bool fresh = IsAlive(claim.Claimant) && claim.Frame == Time.frameCount;
            if (fresh && claim.Distance <= distance)
            {
                return;
            }

            claim.Claimant = claimant;
            claim.Distance = distance;
            claim.Frame = Time.frameCount;
        }

        private static void Take(ref Claim claim, bool rightHand)
        {
            IGrabClaimant claimant = claim.Claimant;
            int frame = claim.Frame;
            claim = default;

            if (frame != Time.frameCount || !IsAlive(claimant))
            {
                return;
            }

            claimant.CommitGrab(rightHand);
        }

        /// <summary>⚠️ An INTERFACE reference does not go through Unity's destroyed-object null: a
        /// claimant killed between the offer and the resolve would still compare non-null and be called
        /// on a dead component.</summary>
        private static bool IsAlive(IGrabClaimant claimant)
        {
            if (claimant == null)
            {
                return false;
            }

            return !(claimant is Object unityObject) || unityObject != null;
        }
    }
}
