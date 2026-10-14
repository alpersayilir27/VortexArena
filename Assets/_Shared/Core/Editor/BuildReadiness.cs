using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using VortexArena.Core.Arena;
using Object = UnityEngine.Object;

namespace VortexArena.Core.Editor
{
    /// <summary>Collects the state of every editor tool that must have run before a build into one
    /// list (the "Hazırlık" section of the <see cref="BuildElementsConfigurator"/> window).</summary>
    /// <remarks>
    /// ⚠️ <b>The checks WRITE NOTHING.</b> Only the window's "Hepsini Çalıştır" button writes, and it
    /// runs all rows, with two exceptions: HMD overlays are installed only when stale (reserializing
    /// the shared rig prefab every run would be merge noise) and the sceneId pass ("Onar") is
    /// button-only (it opens every arena scene). Buttonless rows only report: a ✗ left there is a
    /// human step the tool cannot fix (grip, fire audio, netItemId).
    /// <para>⚠️ Check logic lives in each tool's own file, not here — whoever defines the constants
    /// also answers "is it up to date". A threshold moved here would drift from the tool.</para>
    /// <para>A mode's own row is registered through <see cref="RegisterCheck"/>: Core cannot reference
    /// a mode assembly.</para>
    /// </remarks>
    public static class BuildReadiness
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

        /// <summary>A row registered from outside Core (see <see cref="RegisterCheck"/>).</summary>
        private readonly struct ExtraEntry
        {
            internal ExtraEntry(string title, ReadinessCheck check, string tooltip)
            {
                Title = title;
                Check = check;
                Tooltip = tooltip;
            }

            internal string Title { get; }

            internal ReadinessCheck Check { get; }

            internal string Tooltip { get; }
        }

        private static readonly List<ExtraEntry> Extras = new List<ExtraEntry>();

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
                    "Takımsız harita ↔ taban bölgesi",
                    AreTeamlessBasesClean,
                    null,
                    null,
                    "AKTİF sahne: desteklenen modlarının hepsi takımsız olan haritada BaseZone " +
                    "bulunmamalı. NE ZAMAN: bir sahneyi takımsız moda (zombi gibi) alınca ya da " +
                    "takımlı bir arenayı takımsıza çevirince. ✗ ise insan adımıdır: şerit sahneden " +
                    "ELLE silinir — takımsız modda BaseZoneVisibility onu yalnız gizler, yani " +
                    "sahada hiç hata vermez, ama canlanma kuralı olmayan bir bölge sahnede " +
                    "yaşamaya devam eder."),

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
                    "Eşya alma yolu ↔ prefab",
                    ItemGrabPathGuard.ArePrefabsMatched,
                    // The button WRITES NOTHING — it only dumps every mismatch to the console, because
                    // the row shows one line and the fix is a human step in the prefab.
                    "Konsola dök",
                    ItemGrabPathGuard.LogMismatches,
                    "Eşya tanımındaki 'Alma yolu' ile prefabın gerçekten taşıdığı kavrama bileşenleri. " +
                    "NE ZAMAN: bir eşyanın alma yolunu değiştirince ya da yeni elle tutulan obje " +
                    "ekleyince. ✗ ise insan adımıdır: DistanceGrab OLMAYAN bir eşyanın prefabında " +
                    "mesafeli kavrama bileşeni duruyor — ISDK'da 'alınamaz' filtreyle DEĞİL, bileşenin " +
                    "YOKLUĞUYLA ifade edilir; boş aday listesiyle bile interactor hover'a girer ve " +
                    "kavrama basışı sessizce yenir. Araç düzeltmez (bileşeni silmek istenerek mesafeli " +
                    "alınan bir silahı alınamaz yapardı); bileşen prefabdan elle kaldırılır."),

                Check(
                    "İskelet eklem listesi (iki gövde prefabı)",
                    SkeletonStreamGuard.AreJointListsMatched,
                    null,
                    null,
                    "0x07 iskelet akışının eklem kümesi: LocalBodyAvatar (gönderen) ile RemoteAvatar " +
                    "(alıcı) aynı SDK listesini taşımalı, parmaklar listede OLMAMALI; ayrıca tel eklem " +
                    "listesindeki (SkeletonWire) her indeks iki prefabın JointPairs'inde dolu ve AYNI " +
                    "kemiğe çözülmeli, kalça indeksi kalça kemiği olmalı ve 'Objects To Hide Until " +
                    "Valid' boş kalmalı (§6.9). NE ZAMAN: gövde prefablarından birinin " +
                    "NetworkCharacterRetargeter'ı düzenlenince ya da karakter modeli değişince. ✗ ise " +
                    "insan adımıdır: blob eklemleri indeksle taşıdığı için ayrışma hiçbir yerde hata " +
                    "vermez, yalnız uzak gövdeler bozuk çizilir. Liste RUNTIME'da hesaplanmaz — iki " +
                    "prefabın Inspector'ında düzeltilir."),

                Check(
                    "Ağ nesneleri",
                    BuildElementsConfigurator.AreNetObjectsReady,
                    "Onar",
                    BuildElementsConfigurator.RepairSceneIds,
                    "Kırılabilir/ağ objelerinin tür tablosu (NetObjectKind) ve sahnelerin export edilmiş " +
                    "obje listeleri. NE ZAMAN: yeni tür ekleyince, bir sahneye NetObject koyup sahneyi " +
                    "kaydedince ya da bir sahne dosyası merge/kopya ile değişince. Sahne listesi sahne " +
                    "KAYDINDA yazılır. ✗ iki türlüdür: boş/yinelenen 'kind' ya da türü olmayan obje İNSAN " +
                    "adımıdır (sunucu o objeyi tabloya almaz, sahada 'obje kırılmıyor' diye görünür); " +
                    "listede yinelenen sceneId ise 'Onar' ile düzelir — her arena sahnesini açıp " +
                    "0/çakışan kimlikleri onarır, değişeni kaydeder, obje listelerini tazeler, açık " +
                    "sahneleri geri getirir (açık sahne kirliyse çalışmaz). ⚠️ Hepsini Çalıştır bunu " +
                    "KOŞMAZ; merge sonrası elle bas. Tür yoksa satır temizdir."),

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

            // Mode-owned rows last: Core knows neither their order nor their assemblies.
            AppendExtras(rows);
            return rows;
        }

        /// <summary>Clean when the active scene's map is not team-less, or is team-less and carries no
        /// <see cref="BaseZone"/>.</summary>
        /// <remarks>"Is the map team-less" is asked from <see cref="TemplateBasicsLoader"/> — the tool
        /// that also skips placing the zones, so both sides cannot drift.</remarks>
        private static bool AreTeamlessBasesClean(out string detail)
        {
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !TemplateBasicsLoader.IsTeamlessMapScene(scene.name))
            {
                detail = "aktif sahne takımsız harita değil — denetim geçerli değil.";
                return true;
            }

            BaseZone[] zones =
                Object.FindObjectsByType<BaseZone>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (zones.Length == 0)
            {
                detail = "takımsız haritada taban bölgesi yok.";
                return true;
            }

            detail = $"Takımsız haritada taban bölgesi var (×{zones.Length}, ilki '{zones[0].name}') — " +
                     "sil (zombi gibi takımsız modlarda taban yoktur).";
            return false;
        }

        /// <summary>A check: clean/not + a short human detail. ⚠️ WRITES NOTHING.</summary>
        public delegate bool ReadinessCheck(out string detail);

        /// <summary>Registers a row owned by a mode assembly, appended after the fixed rows.</summary>
        /// <remarks>⚠️ Core cannot reference a mode (the dependency only runs the other way), so a
        /// mode's <c>[InitializeOnLoad]</c> class pushes its check in here. Re-registering the same
        /// title replaces it: a domain reload re-runs every initializer.</remarks>
        public static void RegisterCheck(string title, ReadinessCheck check, string tooltip)
        {
            if (string.IsNullOrEmpty(title) || check == null)
            {
                return;
            }

            for (int i = 0; i < Extras.Count; i++)
            {
                if (Extras[i].Title == title)
                {
                    Extras[i] = new ExtraEntry(title, check, tooltip);
                    return;
                }
            }

            Extras.Add(new ExtraEntry(title, check, tooltip));
        }

        private static void AppendExtras(List<ReadinessRow> rows)
        {
            for (int i = 0; i < Extras.Count; i++)
            {
                ExtraEntry extra = Extras[i];
                rows.Add(Check(extra.Title, extra.Check, null, null, extra.Tooltip));
            }
        }

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
