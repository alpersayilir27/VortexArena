namespace VortexArena.Core.Combat
{
    /// <summary>
    /// Single meeting point for what the LOCAL player holds — source of the
    /// <c>itemL</c>/<c>itemR</c>/<c>gripFlags</c> bytes in the §6.2 pose packet.
    /// <para>WRITER: <c>Weapon</c> / <c>WeaponGranter</c> (Core.Combat) via <see cref="Report"/>.
    /// READER: <c>PlayerPoseTracker</c> (App), into the 20 Hz pose packet.</para>
    /// <para>Why the seam: App must not rediscover the scene's weapons (GetComponentsInChildren /
    /// grab events) to answer "what is in the hand" — the side that knows reports once.</para>
    /// <para>⚠️ Sends nothing and enforces no rule; only holds the last reported state. A send here
    /// would split item state from the pose packet and create a second source of truth.</para>
    /// </summary>
    public static class HeldItems
    {
        /// <summary><c>netItemId</c> of the item in the left hand; <c>0</c> = empty hand.</summary>
        public static byte Left { get; private set; }

        /// <summary><c>netItemId</c> of the item in the right hand; <c>0</c> = empty hand.</summary>
        public static byte Right { get; private set; }

        /// <summary>
        /// Both hands hold the SAME item (<c>FLAG_GRIP_LINKED</c>). ⚠️ "the same id in two slots"
        /// alone does not express this — dual pistols are a legitimate state (§6.6).
        /// </summary>
        public static bool GripLinked { get; private set; }

        /// <summary>Is the main hand the right one (<c>FLAG_PRIMARY_RIGHT</c>). Only meaningful
        /// while <see cref="GripLinked"/>.</summary>
        public static bool PrimaryRight { get; private set; }

        /// <summary>Reports the local hold state (writing side: <c>Weapon</c>/<c>WeaponGranter</c>).</summary>
        public static void Report(byte left, byte right, bool gripLinked, bool primaryRight)
        {
            Left = left;
            Right = right;
            GripLinked = gripLinked;
            PrimaryRight = primaryRight;
        }

        /// <summary>
        /// Resets the state (both hands empty). Called on a scene/map transition: otherwise the old
        /// scene's weapon keeps being reported as "in hand" in the new scene.
        /// </summary>
        public static void Clear()
        {
            Left = 0;
            Right = 0;
            GripLinked = false;
            PrimaryRight = false;
        }
    }
}
