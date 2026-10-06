namespace VortexArena.Core.UI
{
    /// <summary>
    /// Gradient direction of a <see cref="UiShape"/> fill. Prefixed because <c>UnityEngine.GradientMode</c>
    /// makes the bare name ambiguous in any file that imports both namespaces.
    /// ⚠️ Serialized — new values go to the END (Unity stores the numeric index).
    /// </summary>
    public enum UiGradientMode
    {
        None = 0,
        Vertical = 1,
        Horizontal = 2
    }
}
