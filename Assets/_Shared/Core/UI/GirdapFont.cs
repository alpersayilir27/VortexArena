namespace VortexArena.Core.UI
{
    /// <summary>
    /// The theme's font roles. CSS mapping: <c>--fx</c> 700 → <see cref="ChakraBold"/>, 600 →
    /// <see cref="ChakraSemiBold"/>; <c>--fd</c> 800 → <see cref="SairaExtraBold"/>, 700 →
    /// <see cref="SairaBold"/>, 500/600 → <see cref="SairaSemiBold"/>; <c>--fb</c> 500 →
    /// <see cref="BarlowMedium"/>.
    /// <para>⚠️ Serialized — new values go to the END (Unity stores the numeric index).</para>
    /// </summary>
    public enum GirdapFont
    {
        ChakraBold = 0,
        ChakraSemiBold = 1,
        SairaExtraBold = 2,
        SairaBold = 3,
        SairaSemiBold = 4,
        BarlowMedium = 5
    }
}
