namespace VortexArena.App
{
    /// <summary>
    /// Uygulama geneli oturum bilgisi: AppBoot rolü ve sunucu adresini yazar, controller'lar okur.
    /// Sahne bir kabuk controller'ıyla doğrudan oynatılırsa (Editor testi) rol
    /// çözülmemiş olabilir — controller'lar Awake'te kendi varsayılanını yazar.
    /// <para>
    /// **Tek kabuk sahnesi `Lobby`'dir**: admin de oyuncularla aynı sahnede durur ve sunucunun
    /// `load_match`/`return_to_lobby`'siyle onları takip eder (gözlemci görünümü). Admin'e ait
    /// ayrı bir dashboard sahnesi YOKTUR.
    /// </para>
    /// </summary>
    public static class AppSession
    {
        public const string RolePlayer = "player";
        public const string RoleAdmin = "admin";

        /// <summary>
        /// Silah kavraması yakalama rolü — <b>yalnız editör</b>. Ne oyuncu ne gözlemcidir:
        /// build'e girmez, sunucuya bağlanmaz, maç/kalibrasyon akışına hiç katılmaz; tek işi
        /// gözlükte el takibiyle silahın kavrama pozunu yazmaktır.
        /// <para>⚠️ Sahnesi (<see cref="SceneWeaponCalibration"/>) <b>Build Settings'e KONMAZ</b> —
        /// oynanan bir içerik değildir ve katalogda yeri yoktur. Play'e
        /// <c>EditorSceneManager.playModeStartScene</c> ile doğrudan o sahneden girilir
        /// (<c>DevBootstrap</c>). Bunun sonucu: <see cref="AppBoot"/> bu rolü yükleyemez ve
        /// yüklemeye ÇALIŞMAZ — Boot'a düşülmesi zaten yanlış bir yoldan gelindiği anlamına gelir.</para>
        /// </summary>
        public const string RoleWeapon = "weapon";

        public const string SceneBoot = "Boot";
        public const string SceneLobby = "Lobby";

        /// <summary>
        /// Silah kavrama kalibrasyonunun boş sahnesi. ⚠️ Build Settings'te YOKTUR ve eklenmez
        /// (gerekçe <see cref="RoleWeapon"/>); editörde asset adından bulunur.
        /// </summary>
        public const string SceneWeaponCalibration = "WeaponCalibration";

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
