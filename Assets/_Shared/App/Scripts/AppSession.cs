namespace VortexArena.App
{
    /// <summary>
    /// Uygulama geneli oturum bilgisi: AppBoot rolü ve sunucu adresini yazar, controller'lar okur.
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

        /// <summary>Launcher'ın `--server-ip` ile geçtiği adres. Boşsa adres bilinmiyor.</summary>
        public static string ServerIp = "";

        /// <summary>`--server-port` verilmezse ArenaProtocol.CONTROL_PORT ile doldurulur.</summary>
        public static int ServerPort;

        /// <summary>Admin, kullanıcıya hiç adres sormadan bağlanabilir mi?</summary>
        public static bool HasServerEndpoint =>
            !string.IsNullOrEmpty(ServerIp) && ServerPort > 0;
    }
}
