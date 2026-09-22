#nullable enable
using VortexArena.Protocol;

namespace VortexArena.Server.Core;

/// <summary>Head and hand poses of the last accepted pose packet (§6.2), arena space.</summary>
/// <remarks>⚠️ Immutable and replaced as a WHOLE so MatchDirector can read it without taking
/// <c>PoseGate</c>: a reference read is atomic, a multi-field struct read tears. Same mirroring
/// pattern as <see cref="PlayerState.InObstacle"/>, only the payload is wider than a bool.</remarks>
public sealed class HandPoses
{
    public HandPoses(PoseData head, PoseData left, PoseData right)
    {
        Head = head;
        Left = left;
        Right = right;
    }

    public PoseData Head { get; }

    public PoseData Left { get; }

    public PoseData Right { get; }
}
