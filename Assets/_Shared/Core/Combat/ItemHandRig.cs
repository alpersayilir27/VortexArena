using UnityEngine;

namespace VortexArena.Core.Combat
{
    /// <summary>
    /// The SINGLE definition of where the <b>hand models</b> live in a weapon prefab and what they
    /// are named — these nodes are the only hand-authored source of the grip.
    /// <para>Layout: <c>&lt;weapon root&gt;/Hands/Hand_&lt;Kind&gt;</c>. The name lives in one place
    /// because a one-letter divergence between producer (editor tool) and consumer raises no error
    /// — the node is silently NOT FOUND.</para>
    /// <para>⚠️ Only the RIGHT hand is authored; the left is mirrored at bake
    /// (<c>HandPose.CopyFrom(…, mirrorHandedness: true)</c>). Authoring both would describe the
    /// same grip twice and the two would drift apart.</para>
    /// <para>⚠️ NEVER DRAWN IN GAME: disabled when the bake finishes and left disabled. The weapon
    /// also stands in the scene and in remote avatars' hands, so an enabled hand model would appear
    /// floating in the arena. The hand the player sees is the rig's own.</para>
    /// <para>⚠️ Runtime does NOT read this class: runtime data is the bake output
    /// (<c>ItemDefinition</c> grip fields + <c>GripPoses/Pose_*</c>). Reading from here would
    /// require keeping a hidden model alive and make the bake pointless. The only runtime use is
    /// the safety net below.</para>
    /// </summary>
    public static class ItemHandRig
    {
        /// <summary>Name of the node collecting the hand models (a DIRECT child of the weapon root).</summary>
        public const string RootNodeName = "Hands";

        /// <summary>Handedness of the authored hand — the left side is mirrored at bake.</summary>
        public const bool AuthoredHandIsRight = true;

        /// <summary>Hand node name of a grip point: <c>Hand_Primary</c>, <c>Hand_Secondary</c>.</summary>
        public static string NodeName(GripSocketKind kind)
        {
            return $"Hand_{kind}";
        }

        /// <summary>
        /// Finds the hand node under the weapon; <c>null</c> when missing.
        /// <para>⚠️ The tree is NOT scanned: the lookup descends exactly two levels with
        /// <see cref="Transform.Find"/> — a scan would also accept a same-named node that ended up
        /// elsewhere in the weapon. <c>Find</c> also returns inactive children, so the hidden
        /// post-bake node stays re-editable.</para>
        /// </summary>
        public static Transform Find(Transform itemRoot, GripSocketKind kind)
        {
            if (itemRoot == null)
            {
                return null;
            }

            Transform root = itemRoot.Find(RootNodeName);
            return root != null ? root.Find(NodeName(kind)) : null;
        }

        /// <summary>
        /// Disables the hand nodes — a safety net. The bake already does this; it guards a prefab
        /// whose bake was forgotten (or left enabled for editing) from floating a hand in the arena.
        /// </summary>
        /// <returns>Nodes disabled (zero = already disabled or absent).</returns>
        public static int HideAll(Transform itemRoot)
        {
            int hidden = 0;
            hidden += Hide(Find(itemRoot, GripSocketKind.Primary));
            hidden += Hide(Find(itemRoot, GripSocketKind.Secondary));
            return hidden;
        }

        private static int Hide(Transform node)
        {
            if (node == null || !node.gameObject.activeSelf)
            {
                return 0;
            }

            node.gameObject.SetActive(false);
            return 1;
        }
    }
}
