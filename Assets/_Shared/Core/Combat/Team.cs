namespace VortexArena.Core
{
    /// <summary>
    /// Player/marker team. <c>Neutral</c> = teamless (teamless modes, §10.5 <c>teamMode:"none"</c>).
    /// <para>
    /// ⚠ <b>A new value is appended at the END.</b> This enum is SERIALIZED by
    /// <see cref="Arena.BaseZone"/> and Unity stores enums by numeric index; inserting at the
    /// start/middle shifts the values in existing scenes and breaks every arena's base zone teams.
    /// </para>
    /// </summary>
    public enum Team { Red, Blue, Neutral }
}
