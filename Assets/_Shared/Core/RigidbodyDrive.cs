using UnityEngine;

namespace VortexArena.Core
{
    /// <summary>Single writer of the "who moves this body" pair: kinematic flag + interpolation.
    /// <para>⚠️ A kinematic body driven through its Transform every frame (palm, holster, streamed
    /// pose) must NOT interpolate: interpolation rewrites the Transform from the last two physics
    /// poses, so the visual trails and jitters around the hand while the hand itself is on time.
    /// Only a simulated body gains from it (72/90 Hz frames over 50 Hz physics).</para></summary>
    public static class RigidbodyDrive
    {
        public static void SetKinematic(Rigidbody body, bool kinematic)
        {
            if (body == null)
            {
                return;
            }

            body.isKinematic = kinematic;
            body.interpolation = kinematic
                ? RigidbodyInterpolation.None
                : RigidbodyInterpolation.Interpolate;
        }
    }
}
