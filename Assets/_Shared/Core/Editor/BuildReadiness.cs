using System;
using System.Collections.Generic;

namespace VortexArena.Core.Editor
{
    /// <summary>Collects the state of every editor tool that must have run before a build into one
    /// list (the "Hazırlık" section of the <see cref="BuildElementsConfigurator"/> window).</summary>
    /// <remarks>
    /// ⚠️ <b>The checks WRITE NOTHING.</b> Only the window's "Hepsini Çalıştır" button writes, and it
    /// runs all rows; HMD overlays are the one exception, installed only when stale because
    /// reserializing the shared rig prefab every run would be merge noise. Buttonless rows only
    /// report: a ✗ left there is a human step the tool cannot fix (grip, fire audio, netItemId).
    /// <para>⚠️ Check logic lives in each tool's own file, not here — whoever defines the constants
    /// also answers "is it up to date". A threshold moved here would drift from the tool.</para>
    /// </remarks>
    internal static class BuildReadiness
    {
        /// <summary>One readiness row: state + human readable reason + optional trigger.</summary>
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

            /// <summary>Row title (which tool).</summary>
            internal string Title { get; }

            /// <summary>Whether the check is clean.</summary>
            internal bool Ok { get; }

            /// <summary>First mismatch, or a short summary when up to date.</summary>
            internal string Detail { get; }

            /// <summary>Button label; <c>null</c> = no button.</summary>
            internal string ActionLabel { get; }

            /// <summary>Tool the button runs (the writing side).</summary>
            internal Action Action { get; }

            /// <summary>Hover text: what it covers, WHEN it is needed, what breaks if skipped.
            /// </summary>
            /// <remarks>⚠️ The row's only teaching surface — the answer to "I see ✗, now what" goes
            /// here, not into a HelpBox.</remarks>
            internal string Tooltip { get; }
        }

        /// <summary>Builds every readiness row.</summary>
        /// <remarks>⚠️ Each check swallows its own exception: one tool's contract drift must not
        /// stop the window from drawing (the real work, arena sync, would go with it) — the error
        /// shows up in its own row.
        /// <para>⚠️ Row order = RUN order, not importance, so the list reads as what "Hepsini
        /// Çalıştır" does at each step; it changes together with the tool's own flow.</para>
        /// </remarks>
        internal static List<ReadinessRow> Collect()
        {
            var rows = new List<ReadinessRow>
            {
                // Buttonless rows: "Hepsini Çalıştır" runs them all. A per-row button would lie on
                // human steps the tool cannot fix (grip, fire audio, netItemId).
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
                    "İskelet eklem listesi (iki gövde prefabı)",
                    SkeletonStreamGuard.AreJointListsMatched,
                    null,
                    null,
                    "0x07 iskelet akışının eklem kümesi: LocalBodyAvatar (gönderen) ile RemoteAvatar " +
                    "(alıcı) aynı listeyi taşımalı ve parmaklar listede OLMAMALI (§6.9). NE ZAMAN: " +
                    "gövde prefablarından birinin NetworkCharacterRetargeter'ı düzenlenince ya da " +
                    "karakter modeli değişince. ✗ ise insan adımıdır: blob opak olduğu için ayrışma " +
                    "hiçbir yerde hata vermez, yalnız uzak gövdeler bozuk çizilir. Liste RUNTIME'da " +
                    "hesaplanmaz — iki prefabın Inspector'ında düzeltilir."),

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

        /// <summary>Runs a check; on exception the row becomes ✗ with the message as detail.</summary>
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
