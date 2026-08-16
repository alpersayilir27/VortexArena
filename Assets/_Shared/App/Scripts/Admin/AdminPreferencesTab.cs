namespace VortexArena.App.Admin
{
    /// <summary>
    /// Tercihler panelinin sekmeleri — <see cref="AdminPreferencesPanel"/>'deki dizilerin
    /// (düğme/etiket/sayfa) indeksi.
    /// <list type="bullet">
    /// <item><b>Match</b> (MAÇ): ORTAK ayarlar — sunucudaki seçime ve anlık komutlara dokunur,
    /// değişiklik tüm adminlerde görünür.</item>
    /// <item><b>View</b> (GÖRÜNÜM): YEREL ayarlar — yalnız bu ekranı ilgilendirir
    /// (<see cref="AdminSession"/>, <c>PlayerPrefs</c>).</item>
    /// <item><b>Connection</b> (BAĞLANTI): oturumun kendisi — bağlantı durumu, yeniden bağlan/kes
    /// ve oyundan çık.</item>
    /// </list>
    /// ⚠️ Serialize edilen dizilerin indeksidir: yeni sekme <b>SONA</b> eklenir, aksi hâlde
    /// prefabtaki bağlar sessizce kayar.
    /// </summary>
    public enum AdminPreferencesTab
    {
        Match = 0,
        View = 1,
        Connection = 2
    }
}
