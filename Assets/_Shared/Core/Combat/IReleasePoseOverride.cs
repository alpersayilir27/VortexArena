using UnityEngine;

namespace VortexArena.Core.Combat
{
    /// <summary>Last say on the pose a LOCAL release publishes, asked by
    /// <see cref="NetObjectGrabBridge"/> just before <c>object_release</c> goes out.
    /// <para>⚠️ The pose is what every other headset sees the object land on, so a component that must
    /// put the object down somewhere else than the hand left it (beside a station instead of on the
    /// stack it just poured) corrects it HERE: a pose fixed after the release is a second, conflicting
    /// truth. An overridden release is also motionless — a throw arc would carry it straight back.</para></summary>
    public interface IReleasePoseOverride
    {
        /// <summary>True = release at <paramref name="worldPose"/> instead of where the hand left it.</summary>
        bool TryOverrideReleasePose(ref Pose worldPose);
    }
}
