namespace VortexArena.App
{
    /// <summary>
    /// Uygulama geneli oturum bilgisi: AppBoot rolü yazar, controller'lar okur.
    /// Sahne bir kabuk controller'ıyla doğrudan oynatılırsa (Editor testi) rol
    /// çözülmemiş olabilir — controller'lar Awake'te kendi varsayılanını yazar.
    /// </summary>
    public static class AppSession
    {
        public const string RolePlayer = "player";
        public const string RoleAdmin = "admin";

        public const string SceneBoot = "Boot";
        public const string SceneLobby = "Lobby";
        public const string SceneAdminConsole = "AdminConsole";

        public static string Role = RolePlayer;
        public static bool RoleResolved;
    }
}
