using System;
using UnityEngine;

namespace VortexArena.Core.Combat
{
    /// <summary>
    /// A grip record: the CONTROLLER ANCHOR's local POSITION relative to the item
    /// (<c>OVRCameraRig.left/rightHandAnchor</c> — the very pose that travels on the wire) + the
    /// finger pose rigged for this weapon + the hand model's placement on the controller. The only
    /// authored source of the grip; it lives in <see cref="ItemDefinition"/> — there is NO grip node
    /// in the prefab.
    /// <para>⚠️ The ANCHOR record (<see cref="position"/>) carries NO ROTATION and none is added:
    /// the item's rotation is always the main controller's (<see cref="ItemGripSolver"/>), the field
    /// only says WHERE on the item the controller sits. A rotation here would let anyone rotating
    /// the studio root deviate the weapon from the controller in game — symptom "the weapon comes
    /// out wrong depending on hand position", expensive to diagnose. Same for the foregrip.</para>
    /// <para>⚠️ The wrist rotation (<see cref="wristRotation"/>) is not an exception but a DIFFERENT
    /// thing: how the hand model sits on the controller, not where the item goes. Deliberately
    /// separate — the weapon stays controller-aligned while the hand may sit sideways/below.
    /// Changing these fields does NOT affect the item pose; single reader
    /// <c>ItemGripAuthority.ResolveAnchorToWrist</c>.</para>
    /// <para>Authored in the editor studio (controller root seated on the grip), not captured with
    /// the headset. Space is the ANCHOR — the same space the solver and the wire use, so no delta
    /// is needed and a rig-less spectator draws the weapon exactly as the player does.</para>
    /// <para>⚠️ <see cref="position"/> is METRES, NOT scaled by the item's visual scale.
    /// Recomposition is always <c>item.position + item.rotation * position</c>, never
    /// <c>TransformPoint</c>: <c>WPN_*</c> roots are 0.8, so TransformPoint applies the scale twice
    /// and the hand floats beside the weapon. The studio writes it the same symmetric way.</para>
    /// </summary>
    [Serializable]
    public struct ItemGripPose
    {
        // ⚠️ A separate "authored" flag is MANDATORY: zero position is a valid grip (controller at
        // the item's origin — today's default), so "zero = unauthored" would be silently wrong.
        // Deserializes false on never-authored assets.
        [Tooltip("Bu kavrama stüdyoda yazıldı mı. false = hiç yazılmamış (alanların içeriği anlamsız).")]
        public bool authored;

        [Tooltip("Kumanda anchor'ının EŞYAYA göre yerel konumu (metre, ölçeksiz). Dönüş yoktur: silah her " +
                 "zaman kumandayla hizalıdır.")]
        public Vector3 position;

        // ⚠️ The finger pose is RIGGED, not selected: joints rotated by hand in the studio, per
        // weapon. No shared "squeeze/grip" table — grip geometry differs and a shared table left
        // fingers inside the body on some weapons.
        // ⚠️ May be EMPTY: position authored but fingers not rigged yet is valid; that hand stays
        // in the idle pose (HandPoseLibrary.IdleJointRotations).
        [Tooltip("Bu slotta elin riglenmiş parmak duruşu — eklem başına yerel dönüş. Boş = riglenmemiş " +
                 "(el boşta duruşunda kalır).")]
        public HandJointRotation[] fingerJoints;

        // ⚠️ The wrist needs its own "authored" flag: these fields came AFTER the grip record, so
        // older records lack the keys and wristRotation deserializes as (0,0,0,0) — invalid. While
        // the flag is down the read falls back to the shared offset (HandPoseLibrary.AnchorToWrist),
        // so older weapons keep their current hands until authored.
        [Tooltip("El modelinin bu slottaki yerleşimi stüdyoda yazıldı mı. false = yazılmamış " +
                 "(paylaşılan varsayılan ofset kullanılır).")]
        public bool wristAuthored;

        [Tooltip("El modelinin (ISDK bileğinin) KUMANDA ANCHOR'ına göre yerel konumu (metre).")]
        public Vector3 wristPosition;

        [Tooltip("El modelinin KUMANDA ANCHOR'ına göre yerel dönüşü — elin silaha göre yan/alttan " +
                 "durmasını bu taşır. Eşyanın pozunu ETKİLEMEZ.")]
        public Quaternion wristRotation;

        /// <summary>Has this record been authored (its fields are not read when unauthored).</summary>
        public bool IsAuthored => authored;

        /// <summary>Are this slot's fingers rigged (may be empty even when the position is
        /// authored).</summary>
        public bool HasFingers => fingerJoints != null && fingerJoints.Length > 0;

        /// <summary>
        /// Is the hand model's placement authored in this slot.
        /// <para>⚠️ The rotation's validity is tested too: a flag turned on by hand (asset/merge)
        /// with a zero rotation would draw a broken hand; this falls back to the default.</para>
        /// </summary>
        public bool HasWrist =>
            wristAuthored && Quaternion.Dot(wristRotation, wristRotation) > 0.0001f;

        /// <summary>The authored hand placement (only meaningful while <see cref="HasWrist"/>).</summary>
        public Pose Wrist => new Pose(wristPosition, wristRotation);

        /// <summary>
        /// Builds a record from a studio-authored position + hand placement + finger pose.
        /// </summary>
        /// <param name="anchorInItem">Controller anchor local to the ITEM — metres, unscaled (see
        /// class warning).</param>
        /// <param name="wristInAnchor">Hand model local to the controller anchor.</param>
        /// <param name="fingerJoints">Rigged finger joints (may be empty).</param>
        public static ItemGripPose From(in Vector3 anchorInItem, in Pose wristInAnchor,
            HandJointRotation[] fingerJoints)
        {
            return new ItemGripPose
            {
                authored = true,
                position = anchorInItem,
                fingerJoints = fingerJoints,
                wristAuthored = true,
                wristPosition = wristInAnchor.position,
                wristRotation = wristInAnchor.rotation,
            };
        }
    }
}
