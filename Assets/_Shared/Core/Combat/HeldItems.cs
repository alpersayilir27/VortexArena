using UnityEngine;

namespace VortexArena.Core.Combat
{
    /// <summary>
    /// Single meeting point for what the LOCAL player holds: ONE SLOT PER HAND, and the source of the
    /// <c>itemL</c>/<c>itemR</c>/<c>gripFlags</c> bytes in the §6.2 pose packet.
    /// <para>WRITERS are the categories that put something in a hand — the weapon collector
    /// (<c>Weapon.RefreshHeldItems</c>), the wrist holster, later world objects — and they are EQUAL:
    /// each claims its own hand with <see cref="Report"/> and drops it with <see cref="Release"/>.
    /// ⚠️ The wire bytes are DERIVED from the slots, never written separately. With a byte collector
    /// on one category's side, every new kind of held item had to be added to <c>Weapon</c> and to the
    /// finger poser — the bomb already cost both.</para>
    /// <para>READERS: <c>PlayerPoseTracker</c> (App) for the pose packet, <c>HandGripPoser</c> for the
    /// wrist lock and finger pose.</para>
    /// <para>⚠️ Sends nothing and enforces no rule; only holds the last reported state. A send here
    /// would split item state from the pose packet and create a second source of truth.</para>
    /// </summary>
    public static class HeldItems
    {
        /// <summary>What one hand holds; <see cref="Definition"/> <c>null</c> = empty hand.</summary>
        public readonly struct Slot
        {
            /// <summary>Who claimed the hand. The claim can only be dropped by this same object, so no
            /// writer can clear another category's slot.</summary>
            public readonly Object Owner;

            public readonly ItemDefinition Definition;

            /// <summary>The instance IN HAND — the finger poser locks the grip onto it.</summary>
            public readonly Transform Instance;

            /// <summary>Which grip point of the item this hand is on.</summary>
            public readonly GripSocketKind Kind;

            /// <summary>Controller this hand was claimed for; <c>None</c> when it could not be
            /// resolved.
            /// <para>⚠️ Two consumers want DIFFERENT answers here and both are right: the wire must
            /// name a hand even for an unresolved controller (§6.6 — one bit has no "unknown", so an
            /// unresolved hand counts as right), while the POSE must not lock a hand it is unsure
            /// about. Keeping the controller lets one slot answer both.</para></summary>
            public readonly OVRInput.Controller Controller;

            public Slot(Object owner, ItemDefinition definition, Transform instance,
                GripSocketKind kind, OVRInput.Controller controller)
            {
                Owner = owner;
                Definition = definition;
                Instance = instance;
                Kind = kind;
                Controller = controller;
            }

            public bool IsEmpty => Definition == null;

            /// <summary><c>netItemId</c> of the item in this hand; <c>0</c> = nothing on the wire.
            /// ⚠️ An item with no id still FILLS the slot (the hand is not free and the fingers still
            /// grip it) — it is only invisible to remote players.
            /// <para>⚠️ A <see cref="ItemDefinition.IsWorldSingle"/> item reports <c>0</c> <b>on
            /// purpose</b> (§6.6): the remote hand draws that object's own network instance, so a byte
            /// would make the viewer build a SECOND copy — two knives, one of them lagging. The
            /// suppression lives here, at the byte's single source, rather than in each grab path.</para></summary>
            public byte NetItemId =>
                Definition != null && Definition.HasNetItemId && !Definition.IsWorldSingle
                    ? Definition.NetItemId
                    : (byte)0;
        }

        private static Slot _left;
        private static Slot _right;

        /// <summary>What the left hand holds.</summary>
        public static Slot LeftHand
        {
            get
            {
                PruneDead();
                return _left;
            }
        }

        /// <summary>What the right hand holds.</summary>
        public static Slot RightHand
        {
            get
            {
                PruneDead();
                return _right;
            }
        }

        /// <summary><c>netItemId</c> of the item in the left hand; <c>0</c> = empty hand.</summary>
        public static byte Left => LeftHand.NetItemId;

        /// <summary><c>netItemId</c> of the item in the right hand; <c>0</c> = empty hand.</summary>
        public static byte Right => RightHand.NetItemId;

        /// <summary>
        /// Both hands hold the SAME instance (<c>FLAG_GRIP_LINKED</c>). ⚠️ "the same id in two slots"
        /// alone does not express this — dual pistols of one type are a legitimate state (§6.6), and
        /// they are two separate instances.
        /// </summary>
        public static bool GripLinked
        {
            get
            {
                PruneDead();
                return _left.Instance != null && _left.Instance == _right.Instance &&
                       _left.NetItemId != 0;
            }
        }

        /// <summary>Is the main hand the right one (<c>FLAG_PRIMARY_RIGHT</c>) — the linked hold's
        /// right hand sits on the PRIMARY grip. Only meaningful while <see cref="GripLinked"/>.</summary>
        public static bool PrimaryRight => GripLinked && _right.Kind == GripSocketKind.Primary;

        /// <summary>Claims a hand for <paramref name="owner"/>; <c>false</c> when the hand already
        /// holds ANOTHER owner's item (first claim wins).
        /// <para>The caller reports the conflict, not this class: it is the side that knows what it
        /// tried to put there. Re-reporting overwrites the owner's own claim, so a collector can
        /// simply re-run.</para></summary>
        public static bool Report(Object owner, bool rightHand, ItemDefinition definition,
            Transform instance, GripSocketKind kind, OVRInput.Controller controller)
        {
            if (owner == null || definition == null || instance == null)
            {
                return false;
            }

            PruneDead();
            Slot current = rightHand ? _right : _left;
            if (!current.IsEmpty && current.Owner != owner)
            {
                return false;
            }

            var slot = new Slot(owner, definition, instance, kind, controller);
            if (rightHand)
            {
                _right = slot;
            }
            else
            {
                _left = slot;
            }

            return true;
        }

        /// <summary>Drops every claim made by <paramref name="owner"/>.</summary>
        public static void Release(Object owner)
        {
            if (owner == null)
            {
                return;
            }

            if (_left.Owner == owner)
            {
                _left = default;
            }

            if (_right.Owner == owner)
            {
                _right = default;
            }
        }

        /// <summary>Drops every claim made by writers of one category — a collector clears its own
        /// slots before re-reporting, so a holder that left the scene cannot linger in a hand.</summary>
        public static void ReleaseAll<T>() where T : class
        {
            if (_left.Owner is T)
            {
                _left = default;
            }

            if (_right.Owner is T)
            {
                _right = default;
            }
        }

        /// <summary>
        /// Resets the state (both hands empty). Called on a scene/map transition: otherwise the old
        /// scene's item keeps being reported as "in hand" in the new scene.
        /// </summary>
        public static void Clear()
        {
            _left = default;
            _right = default;
        }

        /// <summary>Empties a slot whose owner or instance was destroyed. ⚠️ Without this a weapon
        /// clone destroyed on release would keep its hand claimed and the next item would be refused
        /// — the destroyer is not obliged to release first.</summary>
        private static void PruneDead()
        {
            if (!_left.IsEmpty && (_left.Owner == null || _left.Instance == null))
            {
                _left = default;
            }

            if (!_right.IsEmpty && (_right.Owner == null || _right.Instance == null))
            {
                _right = default;
            }
        }
    }
}
