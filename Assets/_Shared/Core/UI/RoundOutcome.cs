namespace VortexArena.Core.UI
{
    /// <summary>How a finished round went for the LOCAL player — picks the colour of
    /// <see cref="RoundResultBanner"/>. The WORDING stays the mode's; only the tone is chosen here.</summary>
    public enum RoundOutcome
    {
        Won,
        Lost,
        Draw
    }
}
