namespace VortexArena.Core.Combat
{
    /// <summary>Component that carries an item's definition on its own GameObject — the seam the
    /// grip bench uses to resolve "which definition does this prefab write to" without knowing the
    /// item's type.
    /// <para>⚠️ Only for holders whose definition is SERIALIZED on the prefab. A component that
    /// receives its definition at runtime (<c>Throwable.Arm</c>) must NOT implement this: on the
    /// prefab asset it would answer null and read as "resolution failed" — those items are matched
    /// by their definition's own <c>Prefab</c> field instead.</para></summary>
    public interface IItemHolder
    {
        ItemDefinition Definition { get; }
    }
}
