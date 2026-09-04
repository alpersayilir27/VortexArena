namespace VortexArena.App
{
    /// <summary>
    /// App-wide session info: AppBoot writes role + server address, controllers read it.
    /// Playing a scene directly with a shell controller (Editor test) may leave the role
    /// unresolved — controllers write their own default in Awake.
    /// <para>
    /// **The only shell scene is `Lobby`**: the admin stands in the same scene as the players and
    /// follows them via `load_match`/`return_to_lobby`. NO separate admin dashboard scene exists.
    /// </para>
    /// </summary>
    public static class AppSession
    {
        public const string RolePlayer = "player";
        public const string RoleAdmin = "admin";

        public const string SceneBoot = "Boot";
        public const string SceneLobby = "Lobby";

        /// <summary>Player-LOCAL utility scene (on-site venue survey): the headset loads it itself,
        /// it is never server-routed and never a match scene.</summary>
        public const string SceneVenueSurvey = "VenueSurvey";

        public static string Role = RolePlayer;
        public static bool RoleResolved;

        /// <summary>The address the launcher passes via `--server-ip`. Empty means the address is unknown.</summary>
        public static string ServerIp = "";

        /// <summary>Filled with ArenaProtocol.CONTROL_PORT when `--server-port` is not given.</summary>
        public static int ServerPort;

        /// <summary>Can the admin connect without asking the user for an address at all?</summary>
        public static bool HasServerEndpoint =>
            !string.IsNullOrEmpty(ServerIp) && ServerPort > 0;
    }
}
