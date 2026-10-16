namespace VortexArena.Core.Combat
{
    /// <summary>Last say on a BARE-HAND grab, asked by <see cref="NetObjectGrabBridge"/> after the arbiter
    /// picked it and before anything goes on the wire; usually a component on this same object.
    /// <para>⚠️ Local presentation rule, not authority: the server still accepts the grab, and a carrier
    /// (spatula) claims through its own <c>object_grab</c> without asking this.</para></summary>
    public interface IHandGrabGate
    {
        /// <summary>False = this hand may not take it now; the implementer gives the player the reason.</summary>
        bool AllowHandGrab(bool rightHand);
    }
}
