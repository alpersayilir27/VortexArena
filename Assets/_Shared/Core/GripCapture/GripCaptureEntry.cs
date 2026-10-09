using System;
using VortexArena.Core.Combat;

namespace VortexArena.Core.GripCapture
{
    /// <summary>One grip slot captured during a Play session.</summary>
    [Serializable]
    public struct GripCaptureEntry
    {
        public GripSocketKind kind;
        public bool rightHand;
        public ItemGripPose pose;
    }
}
