using System;
using System.Collections.Generic;

namespace VortexArena.Core.Editor
{
    /// <summary>
    /// Build almadan önce çalıştırılmış olması gereken editör araçlarının durumunu TEK listede
    /// toplar (<see cref="BuildElementsConfigurator"/> penceresinin "Hazırlık" bölümü).
    /// <para>
    /// ⚠️ <b>Denetimler HİÇBİR ŞEY YAZMAZ.</b> Yazan tek şey pencerenin "Hepsini Çalıştır"
    /// düğmesidir ve buradaki satırların hepsini koşar; HMD katmanları bunun tek istisnasıdır —
    /// yalnız BAYATKEN kurulur, çünkü paylaşılan rig prefabını her koşuda yeniden serialize etmek
    /// merge gürültüsü olurdu. Düğmesiz satırlar yalnız durum gösterir: orada kalan ✗ aracın
    /// düzeltemeyeceği insan adımıdır (kavrama, ateş sesi, netItemId).
    /// </para>
    /// <para>
    /// ⚠️ <b>Denetimin mantığı burada DEĞİL, aracın kendi dosyasındadır</b> (sabitleri kim
    /// tanımlıyorsa "güncel mi" sorusunu da o cevaplar). Bu sınıf yalnız toplar — buraya taşınan
    /// bir eşik değeri, aracın içindekinden sessizce sapardı.
    /// </para>
    /// </summary>
    internal static class BuildReadiness
    {
        /// <summary>Tek bir hazırlık satırı: durum + insan diliyle gerekçe + (varsa) tetik.</summary>
        internal readonly struct ReadinessRow
        {
            internal ReadinessRow(string title, bool ok, string detail, string actionLabel, Action action, string tooltip)
            {
                Title = title;
                Ok = ok;
                Detail = detail;
                ActionLabel = actionLabel;
                Action = action;
                Tooltip = tooltip;
            }

            /// <summary>Satır başlığı (hangi araç).</summary>
            internal string Title { get; }

            /// <summary>Denetim temiz mi.</summary>
            internal bool Ok { get; }

            /// <summary>İlk uyuşmazlık ya da güncelse kısa özet.</summary>
            internal string Detail { get; }

            /// <summary>Buton yazısı; <c>null</c> = butonsuz satır.</summary>
            internal string ActionLabel { get; }

            /// <summary>Butonun çalıştıracağı araç (yazan taraf).</summary>
            internal Action Action { get; }

            /// <summary>
            /// Satırın üstüne gelince okunan açıklama: ne kapsıyor, <b>NE ZAMAN</b> gerekiyor,
            /// atlanırsa ne kırılıyor. ⚠️ Bu metin satırın TEK öğretici yüzeyidir — "✗ gördüm, ne
            /// yapmalıyım" sorusunun cevabı buraya yazılır, HelpBox'a değil.
            /// </summary>
            internal string Tooltip { get; }
        }

        /// <summary>
        /// Tüm hazırlık satırlarını üretir. ⚠️ <b>Her denetim kendi istisnasını yutar</b>: pencere
        /// tek bir aracın sözleşme kayması yüzünden hiç çizilmez hâle gelirse, asıl işi (arena
        /// senkronu) de yapılamaz olurdu — hata satırın kendisinde görünür.
        /// <para>
        /// ⚠️ <b>Satır sırası = KOŞUM sırasıdır</b>, önem sırası değil: "Hepsini Çalıştır"ın hangi
        /// adımda ne yaptığı listeden okunabilsin diye. Sıra değişecekse aracın kendi akışıyla
        /// birlikte değişir.
        /// </para>
        /// </summary>
        internal static List<ReadinessRow> Collect()
        {
            var rows = new List<ReadinessRow>
            {
                // Düğmesiz satırlar: hepsini "Hepsini Çalıştır" koşuyor. Ayrı bir düğme, aracın
                // düzeltemeyeceği insan adımlarında (kavrama, ateş sesi, netItemId) yalan söylerdi.
                Check(
                    "Arena kayıtları",
                    BuildElementsConfigurator.IsArenaRegistryUpToDate,
                    null,
                    null,
                    "Build Settings + GameCatalog.maps + modların harita listeleri. NE ZAMAN: arena " +
                    "ekleyince/silince/taşıyınca ya da bir sahnenin desteklediği modları değiştirince. " +
                    "Eksik kalırsa harita ne admin seçicisinde ne maps.json'da olur; silinmiş arenanın " +
                    "kalıntı satırı ise APK build'ini 'diskte olmayan sahne' diye iptal ettirir. " +
                    "Hepsini Çalıştır bunu koşar."),

                Check(
                    "Silah kiti",
                    WeaponKitBuilder.AreWeaponsReady,
                    null,
                    null,
                    "WD_* asset'leri, WPN_* prefab bağları, FX/gösterge prefabları, WeaponCatalog. " +
                    "NE ZAMAN: WeaponKitBuilder tablosuna silah ekleyince ya da istatistik/ses profili " +
                    "değiştirince. Hepsini Çalıştır bunu koşar; buradaki ✗ aracın DÜZELTEMEYECEĞİ " +
                    "insan adımıdır — kavrama pozu (Kavrama Pozu Stüdyosu) ve WD_*'a elle sürüklenen " +
                    "ateş sesi."),

                Check(
                    "Rastgele silah havuzları",
                    BuildElementsConfigurator.AreModeLoadoutsUpToDate,
                    null,
                    null,
                    "weaponSource:'random' modlarının loadout listesi = WeaponCatalog'un tamamı. " +
                    "NE ZAMAN: arsenale silah eklenince. ✗ ise havuz katalogtan geride kalmıştır ve " +
                    "belirtisi yalnızca 'bazı silahlar oyunda hiç gelmiyor'dur. Liste ELLE düzenlenmez; " +
                    "Hepsini Çalıştır geri yazar."),

                Check(
                    "Net eşya kataloğu",
                    NetItemIdGuard.IsCatalogUpToDate,
                    null,
                    null,
                    "Eşyaların ağ kimlikleri (netItemId). NE ZAMAN: yeni WD_*/eşya eklenince. " +
                    "Hepsini Çalıştır bunu koşar; ✗ ise atanmamış ya da çakışan kimlik var ve o eşya " +
                    "uzak oyuncunun elinde çizilmez."),

                Check(
                    "Sunucu harita tablosu (maps.json)",
                    ServerConfigExporter.IsMapsJsonUpToDate,
                    "Export",
                    () => ServerConfigExporter.Export(false),
                    "Sunucunun okuduğu harita/mod tablosu. NE ZAMAN: harita ya da mod eşlemesi " +
                    "değişince. Hepsini Çalıştır bunu koşar. ⚠️ Dosya tazelenince SUNUCUYU yeniden " +
                    "başlat — çalışan sunucu tabloyu açılışta okur. Bayatsa start_match 'harita bu " +
                    "modu desteklemiyor' der."),

                Check(
                    "HMD katmanları (rig prefabı)",
                    HmdOverlayBuilder.IsRigUpToDate,
                    "Kur",
                    HmdOverlayBuilder.BuildOverlays,
                    "Ekran katmanları: iki uyarı yazısı + hasar vinyeti. NE ZAMAN: pratikte tek " +
                    "seferlik — paylaşılan rig prefabına yazar, tüm arenalara birden gider. " +
                    "Çalıştırılmadıkça yazı ve vinyet hiç çizilmez. Hepsini Çalıştır bunu YALNIZ " +
                    "bayatken koşar (her seferinde yazmak merge gürültüsü olurdu); 'Kur' zorlar."),
            };

            return rows;
        }

        /// <summary>Denetimi koşar; istisna atarsa satır "✗" olur ve mesaj detaya düşer.</summary>
        private delegate bool ReadinessCheck(out string detail);

        private static ReadinessRow Check(
            string title, ReadinessCheck check, string actionLabel, Action action, string tooltip)
        {
            try
            {
                bool ok = check(out string detail);
                return new ReadinessRow(title, ok, detail ?? string.Empty, actionLabel, action, tooltip);
            }
            catch (Exception e)
            {
                return new ReadinessRow(
                    title, false, "denetim hata verdi: " + e.Message, actionLabel, action, tooltip);
            }
        }
    }
}
