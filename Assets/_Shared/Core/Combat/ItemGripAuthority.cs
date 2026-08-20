using UnityEngine;
using VortexArena.Core.Player;

namespace VortexArena.Core.Combat
{
    /// <summary>
    /// Bridge between the controller ANCHOR and the ISDK WRIST — <b>visual hand only</b>.
    /// <para>The grip record is in ANCHOR space (<see cref="ItemGripPose"/>), the same space the
    /// solver (<see cref="ItemGripSolver"/>) and the wire (§6.6) use — so no delta is measured for
    /// the weapon's pose, and a rig-less spectator draws it exactly as the player does.</para>
    /// <para>The delta is needed only for the HAND VISUAL and THREE sides read it: the synthetic
    /// wrist lock (<c>HandGripPoser</c>), the studio ghost hand (<c>GripPoseStudio</c>) and the
    /// remote avatar's wrist (<c>RemoteAvatar.TryResolveGripWrist</c>). ⚠️ Skipping it on the remote
    /// end = "the same weapon held two ways on two screens".</para>
    /// <para>⚠️ The delta is DEFINED, not measured (<see cref="HandPoseLibrary.AnchorToWrist"/>);
    /// no measurement step comes back. An unlocked wrist would come from Meta's synthesized
    /// "natural" pose, whose offset we know nowhere, while the weapon comes from the anchor — two
    /// references, i.e. workbench and game diverging.</para>
    /// <para>The delta may be authored PER SLOT (<see cref="ItemGripPose.Wrist"/>): how the hand
    /// sits varies from grip to grip, the weapon's position relative to the controller does not. An
    /// unauthored slot falls back to the shared definition.</para>
    /// <para>⚠️ No scale trap: the record is unscaled metres, never multiplied by the item's visual
    /// scale (<c>WPN_*</c> roots are 0.8).</para>
    /// <para>⚠️ Left and right resolve SEPARATELY — the grip is not symmetric.</para>
    /// <para>⚠️ No protocol counterpart; the wire format does not change (§6.6). The wire carries
    /// the wrist pose + which item is held; how the hand is posed is drawn from here.</para>
    /// </summary>
    public static class ItemGripAuthority
    {
        /// <summary>
        /// Anchor→ISDK-wrist delta (anchor space, metres); single source
        /// <see cref="HandPoseLibrary.AnchorToWrist"/>.
        /// <para>⚠️ Looks redundant but stays: two readers (local wrist lock, studio) share one
        /// gate, so neither is forgotten if the offset's definition changes.</para>
        /// </summary>
        public static Pose ResolveAnchorToWrist(bool rightHand)
        {
            return HandPoseLibrary.AnchorToWrist(rightHand);
        }

        /// <summary>
        /// A grip record's own hand placement; falls back to the shared definition when unauthored.
        /// <para>⚠️ The fallback stays SILENT: hand placement was added after the grip record, so
        /// every older record keeps its current hand through this path. A warning here would fire
        /// for all 13 weapons, none of them broken; a missing placement shows in the studio.</para>
        /// </summary>
        public static Pose ResolveAnchorToWrist(in ItemGripPose grip, bool rightHand)
        {
            return grip.HasWrist ? grip.Wrist : ResolveAnchorToWrist(rightHand);
        }

        /// <summary>
        /// WRIST pose from a WORLD anchor pose (<c>wrist = anchor ∘ delta</c>).
        /// <para>Both wrist locks (<c>HandGripPoser</c>) go through here: the record states the
        /// anchor, the synthetic hand is given the wrist. Composed by hand (NOT
        /// <c>TransformPoint</c>) — the delta is in metres.</para>
        /// </summary>
        /// <param name="anchorToWrist">Slot's own placement or the shared definition
        /// (<c>ResolveAnchorToWrist</c>).</param>
        public static Pose WristFromAnchor(in Pose anchorWorld, in Pose anchorToWrist)
        {
            return new Pose(
                anchorWorld.position + anchorWorld.rotation * anchorToWrist.position,
                anchorWorld.rotation * anchorToWrist.rotation);
        }
    }
}
