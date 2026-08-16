using UnityEngine;

namespace VortexArena.App
{
    /// <summary>
    /// Oturum boyu yaşayan App tekillerinin <b>TEK kurulum noktası</b>: hangi tekilin doğacağına
    /// burada, bir kez karar verilir.
    ///
    /// <para>
    /// <b>Neden tek yerde:</b> bu tekiller sahneye KONMAZ, kendi kendilerine doğar
    /// (<c>DontDestroyOnLoad</c>). Her biri kendi <c>RuntimeInitializeOnLoadMethod</c>'unu taşısaydı
    /// "bu oturumda hangileri gerekli" sorusunun cevabı N ayrı dosyaya dağılırdı: yeni bir oturum
    /// türü (test kipi, araç kipi, yeni bir rol) eklemek N dosyayı tek tek düzenlemek olur, biri
    /// atlandığında da hata vermez — yalnız o tekil beklenmedik bir yerde belirirdi. Liste burada
    /// durduğu sürece yeni bir oturum türü <b>tek satırlık</b> bir koşuldur.
    /// </para>
    ///
    /// <para>
    /// ⚠️ <b>Tekillerin kendi <c>Install</c>'ları koşulsuzdur</b> ve öyle kalmalı: "gerekli mi"
    /// sorusunu tekilin kendisi cevaplarsa kapı yine dağılır. Tek istisna, tekilin kendi
    /// <i>varlık</i> koşuludur (ör. <c>AdminSpectator</c> Android'de hiç doğmaz — bu bir oturum
    /// kararı değil, o sınıfın platform gerçeği).
    /// </para>
    ///
    /// <para>
    /// ⚠️ <b>Sıra ÖNEMSİZDİR ve öyle kalmalı:</b> tekiller birbirini <c>Install</c> anında
    /// çağırmaz, olaylara abone olup beklerler. Biri diğerinden önce doğmak zorunda kalırsa o
    /// bağımlılık burada gizli bir kurala dönüşür — bunun yerine geç bağlanma (olay/tembel arama)
    /// kullanılır.
    /// </para>
    ///
    /// <para>
    /// <b>Kapsam:</b> yalnız <c>VortexArena.App</c> tekilleri. Core'un kendi tekilleri
    /// (<c>HandGripPoser</c>, <c>WeaponGranter</c>, <c>ObstacleViolationProbe</c> …) buraya
    /// GİRMEZ — Core, App'i referanslamaz ve oturum rolünü bilmez.
    /// </para>
    ///
    /// <para>
    /// Bugün her oturum türü (player, admin) aynı listeyi kurar; kapı bu yüzden koşulsuzdur.
    /// Sunucusuz bir oturum türü gelirse koşul <b>buraya</b>, tek satır olarak girer — tekillere
    /// dağıtılmaz. Rol bu noktada KESİN bilinir: <c>DevSession</c>/<c>AppBoot</c> rolü
    /// <c>BeforeSceneLoad</c>'da yazar.
    /// </para>
    /// </summary>
    internal static class AppSingletons
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            InstallNetworkSingletons();
        }

        /// <summary>
        /// Sunucuya bağlı bir oturumun tekilleri: bağlantı/yükleme/maç sonu kartları, kimlik
        /// göstergesi, atılma kapanışı, sahne yönlendirmesi ve admin gözlemcisi.
        /// </summary>
        private static void InstallNetworkSingletons()
        {
            ConnectionOverlay.Install();
            LoadingOverlay.Install();
            MatchResultOverlay.Install();
            IdentifyOverlay.Install();
            KickedShutdown.Install();
            SceneRouter.Install();
            Admin.AdminSpectator.Install();
        }
    }
}
