namespace VortexArena.Core.Combat
{
    /// <summary>Which rule wrote <see cref="PlayerCombatState.StatusText"/>; the HUD picks its icon by it.</summary>
    public enum CombatStatusKind
    {
        None,
        Calibration,
        ModePrompt,
        DeadWait,
        Obstacle,
        DeadCountdown,
        HoldStill,
        Reviving,
        ReturnBase,
        SpawnProtection,

        /// <summary>Downed, waiting for a teammate to lift us (<see cref="ModeReviveAnchor.Teammate"/>).
        /// ⚠️ Appended at the END (serialized enum rule).</summary>
        WaitTeammate,
    }
}
