namespace VortexArena.App.Admin
{
    /// <summary>
    /// Preferences panel tabs — index into <see cref="AdminPreferencesPanel"/>'s
    /// button/label/page arrays.
    /// <list type="bullet">
    /// <item><b>Match</b>: SHARED settings — server-side selection, visible on every admin.</item>
    /// <item><b>View</b>: LOCAL settings (<see cref="AdminSession"/>, <c>PlayerPrefs</c>).</item>
    /// <item><b>Connection</b>: connection state, reconnect/disconnect, quit.</item>
    /// <item><b>Audio</b>: LOCAL mix (<see cref="AdminSession"/> → <c>AudioMix</c>) — affects this
    /// operator's speakers only.</item>
    /// </list>
    /// ⚠️ Index into serialized arrays: append new tabs at the <b>END</b>, else the prefab
    /// bindings silently shift.
    /// </summary>
    public enum AdminPreferencesTab
    {
        Match = 0,
        View = 1,
        Connection = 2,
        Audio = 3
    }
}
