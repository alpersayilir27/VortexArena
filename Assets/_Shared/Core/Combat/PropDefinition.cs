using UnityEngine;

namespace VortexArena.Core.Combat
{
    /// <summary>
    /// A world PROP that can be held: no weapon, no throwable — a knife, a serving board, a burger
    /// ingredient. Adds nothing to <see cref="ItemDefinition"/>; everything a prop needs (grip pose,
    /// grab path, instancing, release axis) already lives in the base.
    /// <para>⚠️ It exists because <see cref="ItemDefinition"/> is abstract ON PURPOSE: every asset must
    /// declare what kind of item it is, and reusing <c>ThrowableDefinition</c> for a lettuce leaf would
    /// hand it a fuse and a blast radius that nothing reads.</para>
    /// <para>Props are typically <c>WorldSingle</c> (§10.10): one instance in the world whose ownership
    /// is passed around, so their <c>itemL</c>/<c>itemR</c> byte stays <c>0</c> (§6.6) and they get no
    /// <c>netItemId</c>.</para>
    /// </summary>
    [CreateAssetMenu(fileName = "Prop", menuName = "VortexArena/Prop Definition")]
    public class PropDefinition : ItemDefinition
    {
    }
}
