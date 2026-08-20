using Oculus.Interaction;
using UnityEngine;

namespace VortexArena.Core.Combat
{
    /// <summary>
    /// A do-nothing ISDK <see cref="ITransformer"/>: freezes the grabbed object in place. Used on
    /// the source weapon the <see cref="WeaponFrame"/> sits on.
    /// <para>⚠️ Omitting the transformer means FREE movement, not immobility: with BOTH the single-
    /// and two-hand lists empty, <c>Grabbable.Start</c> attaches a <c>GrabFreeTransformer</c> itself
    /// ("Create missing defaults").</para>
    /// <para>Not "rewrite the pose every frame": that races ISDK's transformer — who writes last is
    /// up to Unity's call order and the weapon jitters. The only certain fix is disabling the code
    /// that would move it.</para>
    /// </summary>
    public class FrozenGrabTransformer : MonoBehaviour, ITransformer
    {
        public void Initialize(IGrabbable grabbable)
        {
        }

        public void BeginTransform()
        {
        }

        public void UpdateTransform()
        {
        }

        public void EndTransform()
        {
        }
    }
}
