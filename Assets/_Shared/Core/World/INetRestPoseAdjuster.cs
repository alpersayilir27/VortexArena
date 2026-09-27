using UnityEngine;
using VortexArena.Net;

namespace VortexArena.Core.World
{
    /// <summary>Last say on the pose an object comes to rest at, asked on the OWNING client just before
    /// <c>object_rest</c> goes out (<see cref="NetObjectPoseSender"/>).
    /// <para>⚠️ The published pose is what every other headset will see forever, so a placement that must
    /// be exact (a stack layer, a socket) is corrected HERE, not after the fact: a pose fixed once the
    /// rest is on the wire is a second, conflicting truth.</para></summary>
    public interface INetRestPoseAdjuster
    {
        /// <summary>True = send <paramref name="worldPose"/> instead of the physics result.</summary>
        bool TryAdjustRestPose(NetObject net, ref Pose worldPose);
    }
}
