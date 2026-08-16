using System;
using System.Collections.Generic;

namespace VortexArena.Core.Editor
{
    /// <summary>
    /// Build almadan önce çalıştırılmış olması gereken editör araçlarının durumunu TEK listede
    /// toplar (<see cref="BuildElementsConfigurator"/> penceresinin "Hazırlık" bölümü).
    /// <para>
    /// ⚠️ <b>Denetimler HİÇBİR ŞEY YAZMAZ.</b> Yazma iki yoldan olur: silah kiti ve net eşya
    /// kataloğu her eşitlemede kendiliğinden koşar (<c>BuildElementsConfigurator.SyncWeaponKit</c>;
    /// satırları düğmesizdir, kalan ✗ insan adımıdır — kavrama, ateş sesi, netItemId), rig
    /// prefabına yazan HMD katmanları ise düğmeyle, kullanıcı isteyince (paylaşımlı rig prefabını
    /// her eşitlemede yeniden serialize etmek merge gürültüsü olurdu).
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
            internal ReadinessRow(string title, bool ok, string detail, string actionLabel, Action action)
            {
                Title = title;
                Ok = ok;
                Detail = detail;
                ActionLabel = actionLabel;
                Action = action;
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
        }

        /// <summary>
        /// Tüm hazırlık satırlarını üretir. ⚠️ <b>Her denetim kendi istisnasını yutar</b>: pencere
        /// tek bir aracın sözleşme kayması yüzünden hiç çizilmez hâle gelirse, asıl işi (arena
        /// senkronu) de yapılamaz olurdu — hata satırın kendisinde görünür.
        /// </summary>
        internal static List<ReadinessRow> Collect()
        {
            var rows = new List<ReadinessRow>
            {
                Check(
                    "HMD katmanları (rig prefabı)",
                    HmdOverlayBuilder.IsRigUpToDate,
                    "Kur",
                    HmdOverlayBuilder.BuildOverlays),

                // Düğmesiz: ikisi de her eşitlemede koşuyor (SyncWeaponKit). Burada kalan ✗ aracın
                // düzeltemeyeceği insan adımıdır — satır onu okutur, düğme yalan söylerdi.
                Check(
                    "Net eşya kataloğu",
                    NetItemIdGuard.IsCatalogUpToDate,
                    null,
                    null),

                Check(
                    "Silah kiti",
                    WeaponKitBuilder.AreWeaponsReady,
                    null,
                    null),

                Check(
                    "Sunucu harita tablosu (maps.json)",
                    ServerConfigExporter.IsMapsJsonUpToDate,
                    "Export",
                    () => ServerConfigExporter.Export(false)),
            };

            return rows;
        }

        /// <summary>Denetimi koşar; istisna atarsa satır "✗" olur ve mesaj detaya düşer.</summary>
        private delegate bool ReadinessCheck(out string detail);

        private static ReadinessRow Check(string title, ReadinessCheck check, string actionLabel, Action action)
        {
            try
            {
                bool ok = check(out string detail);
                return new ReadinessRow(title, ok, detail ?? string.Empty, actionLabel, action);
            }
            catch (Exception e)
            {
                return new ReadinessRow(title, false, "denetim hata verdi: " + e.Message, actionLabel, action);
            }
        }
    }
}
