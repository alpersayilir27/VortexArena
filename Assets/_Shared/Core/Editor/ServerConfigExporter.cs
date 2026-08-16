using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using VortexArena.Core.Arena;
using VortexArena.Core.Combat;

namespace VortexArena.Core.Editor
{
    /// <summary>
    /// <c>Tools &gt; VortexArena &gt; Server &gt; Export Server Config</c> — projedeki
    /// <see cref="MapDefinition"/> ScriptableObject'lerinden sunucunun okuduğu
    /// <c>Server/config/maps.json</c> dosyasını üretir.
    /// <para>
    /// <b>SİLAH EXPORT'U YOKTUR</b> (Docs/ArenaNet-Protokol.md §10.3): sunucu silah tablosu
    /// tutmaz, hasarı istemci hesaplayıp <c>hit_report.damage</c> ile bildirir ve sunucu aynen
    /// uygular. <see cref="WeaponDefinition"/> SO'ları yalnız istemcide yaşar; silah ekleyip
    /// değiştirdikten sonra export çalıştırmak GEREKMEZ. Bu araç yalnız harita kataloğu içindir
    /// (sunucu <c>start_match</c>'te <c>sceneName</c>'in var olduğunu ve modu desteklediğini
    /// buradan doğrular — başka bir şey okumaz).
    /// </para>
    /// <para>
    /// <b>Determinizm (git diff'i temiz kalsın):</b> haritalar <c>sceneName</c> ve mod listeleri
    /// Ordinal alfabetik sıralanır; satır sonu LF, kodlama UTF-8 BOM'suz, dosya sonunda tek
    /// <c>\n</c>. Aynı içerik → aynı bayt.
    /// </para>
    /// <para>
    /// <b>Güvenlik freni:</b> hiç SO bulunamazsa dosya YAZILMAZ (mevcut sunucu yapılandırması
    /// boş bir tabloyla ezilmesin) — bunun yerine uyarı döner.
    /// </para>
    /// </summary>
    public static class ServerConfigExporter
    {
        private const string MapsFileName = "maps.json";

        /// <summary>Sunucunun okuduğu config klasörü (repo kökü altında <c>Server/config</c>).</summary>
        private static string ConfigDirectory =>
            Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Server", "config"));

        /// <summary>Menü girişi — dialoglu (elle) export.</summary>
        [MenuItem("Tools/VortexArena/Server/Export Server Config", false, 60)]
        private static void ExportMenu()
        {
            Export(true);
        }

        /// <summary>
        /// maps.json üretir ve sonucu döner.
        /// <paramref name="showDialog"/> <c>false</c> iken HİÇBİR dialog açılmaz
        /// (MCP / batch otomasyonundan başlıksız çağrılabilir); özet her hâlükârda
        /// <see cref="Debug.Log"/> ile konsola yazılır.
        /// </summary>
        /// <param name="showDialog">Bitişte özet dialogu gösterilsin mi.</param>
        /// <returns>Yazılan yol, satır sayısı ve doğrulama uyarıları.</returns>
        public static ServerConfigExportResult Export(bool showDialog)
        {
            var result = new ServerConfigExportResult();

            string configDir = ConfigDirectory;
            result.MapsPath = Path.Combine(configDir, MapsFileName);

            List<MapDefinition> maps = CollectMaps(result);

            result.MapCount = maps.Count;

            Directory.CreateDirectory(configDir);

            if (maps.Count > 0)
            {
                WriteFile(result.MapsPath, BuildMapsJson(maps));
            }
            else
            {
                result.Warnings.Add($"Hiç MapDefinition bulunamadı — {MapsFileName} YAZILMADI (mevcut sunucu tablosu korundu).");
            }

            result.Summary =
                $"Export Server Config: {result.MapCount} harita → {configDir} ({result.Warnings.Count} uyarı)";

            for (int i = 0; i < result.Warnings.Count; i++)
            {
                Debug.LogWarning($"[ExportServerConfig] {result.Warnings[i]}");
            }

            Debug.Log($"[ExportServerConfig] {result.Summary}");

            if (showDialog)
            {
                EditorUtility.DisplayDialog("VortexArena — Export Server Config", BuildDialogText(result), "Tamam");
            }

            return result;
        }

        // -------------------------------------------------------------- denetim

        /// <summary>
        /// Diskteki <c>maps.json</c> projedeki haritalarla aynı mı — <b>HİÇBİR ŞEY YAZMAZ</b>
        /// (build hazırlık panelinin okuduğu denetim).
        /// <para>
        /// ⚠️ Karşılaştırma <b>ayrıştırma değil, üretilecek içeriğin kendisidir</b>: export zaten
        /// deterministik (sıralı, LF, BOM'suz) yazıyor, yani "aynı bayt mı" sorusu tam olarak
        /// "export bu dosyayı değiştirir mi" sorusudur. Elle yazılmış bir JSON okuyucu ikinci
        /// (ve sapabilen) bir biçim yorumu olurdu.
        /// </para>
        /// <para>
        /// Uyarılar da hazırlık sorunudur (ör. Build Settings'te olmayan sahne): dosya güncel olsa
        /// bile uyarı varsa satır TEMİZ sayılmaz.
        /// </para>
        /// </summary>
        internal static bool IsMapsJsonUpToDate(out string detail)
        {
            string path = Path.Combine(ConfigDirectory, MapsFileName);
            if (!File.Exists(path))
            {
                detail = $"'{MapsFileName}' YOK — sunucu hiçbir haritayı tanımaz, start_match reddedilir.";
                return false;
            }

            var probe = new ServerConfigExportResult();
            List<MapDefinition> maps = CollectMaps(probe);

            if (maps.Count == 0)
            {
                detail = "Projede export edilebilir harita yok — export dosyaya dokunmaz.";
                return false;
            }

            if (!string.Equals(BuildMapsJson(maps), File.ReadAllText(path), StringComparison.Ordinal))
            {
                detail = $"'{MapsFileName}' projeden ayrışmış ({maps.Count} harita bekleniyor) — export ezecek.";
                return false;
            }

            if (probe.Warnings.Count > 0)
            {
                detail = $"{maps.Count} harita yazılı ama {probe.Warnings.Count} uyarı var " +
                         "(uyarıları görmek için export'u çalıştır).";
                return false;
            }

            detail = $"{maps.Count} harita güncel.";
            return true;
        }

        // -------------------------------------------------------------- toplama

        /// <summary>Tüm projedeki harita tanımlarını toplar, doğrular ve sceneName'e göre sıralar.</summary>
        private static List<MapDefinition> CollectMaps(ServerConfigExportResult result)
        {
            var loaded = new List<MapDefinition>();
            var paths = new Dictionary<MapDefinition, string>();

            string[] guids = AssetDatabase.FindAssets("t:MapDefinition");
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                var asset = AssetDatabase.LoadAssetAtPath<MapDefinition>(path);
                if (asset == null)
                {
                    continue;
                }

                loaded.Add(asset);
                paths[asset] = path;
            }

            loaded.Sort((a, b) =>
            {
                int byName = string.CompareOrdinal(a.SceneName ?? string.Empty, b.SceneName ?? string.Empty);
                return byName != 0 ? byName : string.CompareOrdinal(paths[a], paths[b]);
            });

            Dictionary<string, bool> buildScenes = CollectBuildSettingsScenes();

            var accepted = new List<MapDefinition>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < loaded.Count; i++)
            {
                MapDefinition map = loaded[i];
                string path = paths[map];
                string sceneName = map.SceneName;

                if (string.IsNullOrWhiteSpace(sceneName))
                {
                    result.Warnings.Add($"Boş sceneName: '{path}' — atlandı (katalog anahtarı zorunlu).");
                    continue;
                }

                // ⚠️ Eleme, YİNELEME kontrolünden ÖNCE gelir: elenen harita `seen`'e adını
                // yazmamalı. Yazsaydı, şablonla aynı sahne adını taşıyan GERÇEK bir harita
                // "yinelenen" diye sessizce düşerdi (sıralama şablonu öne alıyor).

                // Şablon = oynanmaz içerik: sessizce atlanır (uyarı gürültü olurdu, orada olması normal).
                if (path.StartsWith(TemplateRoot, StringComparison.Ordinal))
                {
                    continue;
                }

                // Mekan dışında kalan harita EXPORT EDİLMEZ. Sebep: sunucu açılışta operatöre
                // mekan listesi sorar ve o liste bu dosyadan gelir — mekansız bir harita orada
                // gerçekte var olmayan bir işletme satırı açardı. Yeri klasördür, alan değil.
                if (!path.StartsWith(VenuesRoot, StringComparison.Ordinal))
                {
                    result.Warnings.Add(
                        $"'{sceneName}' bir mekan klasöründe değil ('{path}') — ATLANDI. " +
                        $"Oynanacak arenanın MapDefinition'ı {VenuesRoot}<İşletme>/Scenes/<Sahne>/Data/<Sahne>.asset " +
                        $"olmalı; şablonların yeri {TemplateRoot}.");
                    continue;
                }

                if (!seen.Add(sceneName))
                {
                    result.Warnings.Add($"Yinelenen sceneName '{sceneName}': '{path}' — atlandı (ilk eşleşme yazıldı).");
                    continue;
                }

                if (!buildScenes.TryGetValue(sceneName, out bool enabled))
                {
                    result.Warnings.Add($"sceneName '{sceneName}' Build Settings'te YOK — istemciler bu sahneyi yükleyemez ('{path}').");
                }
                else if (!enabled)
                {
                    result.Warnings.Add($"sceneName '{sceneName}' Build Settings'te var ama KAPALI (enabled=false) — '{path}'.");
                }

                accepted.Add(map);
            }

            return accepted;
        }

        /// <summary>
        /// Oynanacak arenaların kökü — mekan adı bu yolun bir alt seviyesinden okunur:
        /// <c>Assets/Arenas/Venues/&lt;İşletme&gt;/Scenes/&lt;Sahne&gt;/Data/&lt;Sahne&gt;.asset</c>.
        /// </summary>
        public const string VenuesRoot = "Assets/Arenas/Venues/";

        /// <summary>
        /// Referans şablonların kökü (<c>Assets/Arenas/Template/Scenes/&lt;Sahne&gt;/…</c>) —
        /// buradaki haritalar export EDİLMEZ.
        /// </summary>
        public const string TemplateRoot = "Assets/Arenas/Template/";

        /// <summary>
        /// Haritanın MEKANI — asset yolundan türetilir, ayrı bir alan yoktur.
        /// <para>
        /// <c>Assets/Arenas/Venues/&lt;İşletme&gt;/…</c> → <c>&lt;İşletme&gt;</c>. Klasör yerleşimi
        /// zaten mekanı anlatıyor (CLAUDE.md: "işletme klasörü kutuların kabıdır"); ikinci bir alan
        /// eklemek onu unutulabilir hâle getirirdi — bir haritayı yanlış mekana yazmanın tek yolu
        /// onu yanlış klasöre koymaktır, o da gözle görülür.
        /// </para>
        /// <para>
        /// Mekan dışındaki haritalar buraya HİÇ GELMEZ: <see cref="CollectMaps"/> onları eler.
        /// </para>
        /// </summary>
        private static string VenueOf(MapDefinition map)
        {
            string path = AssetDatabase.GetAssetPath(map);
            string rest = path.Substring(VenuesRoot.Length);
            int slash = rest.IndexOf('/');
            return slash > 0 ? rest.Substring(0, slash) : rest;
        }

        /// <summary>Build Settings'teki sahne adları → enabled bayrağı (ad çakışırsa açık olan kazanır).</summary>
        private static Dictionary<string, bool> CollectBuildSettingsScenes()
        {
            var scenes = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            EditorBuildSettingsScene[] all = EditorBuildSettings.scenes;

            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] == null || string.IsNullOrEmpty(all[i].path))
                {
                    continue;
                }

                string name = Path.GetFileNameWithoutExtension(all[i].path);
                if (scenes.TryGetValue(name, out bool existing))
                {
                    scenes[name] = existing || all[i].enabled;
                }
                else
                {
                    scenes[name] = all[i].enabled;
                }
            }

            return scenes;
        }

        // ----------------------------------------------------------------- json

        /// <summary>
        /// <c>{ "maps": [ { "sceneName": "Arena12x12", "venue": "Outdoor12x12", "modes": ["tdm"] } ] }</c>
        /// — sunucunun <c>start_match</c> doğrulaması ve <b>mekan seçimi</b> için.
        /// <para>
        /// <b>Arena ÖLÇÜSÜ yazılmaz.</b> Sunucu metre bilmez (pozlar istemci-otoriter, arena
        /// uzayında gelir); ayrıca her işletmenin alanı farklı ve çoğu kare/dikdörtgen bile
        /// değil, yani tek bir ölçü çifti o arenayı tarif etmez.
        /// </para>
        /// </summary>
        private static string BuildMapsJson(List<MapDefinition> maps)
        {
            var sb = new StringBuilder();
            sb.Append("{\n");

            if (maps.Count == 0)
            {
                sb.Append("  \"maps\": []\n");
            }
            else
            {
                sb.Append("  \"maps\": [\n");
                for (int i = 0; i < maps.Count; i++)
                {
                    MapDefinition map = maps[i];
                    sb.Append("    { \"sceneName\": \"").Append(EscapeJson(map.SceneName))
                        .Append("\", \"venue\": \"").Append(EscapeJson(VenueOf(map)))
                        .Append("\", \"modes\": ").Append(BuildModesArray(map.SupportedModeIds))
                        .Append(" }")
                        .Append(i < maps.Count - 1 ? ",\n" : "\n");
                }

                sb.Append("  ]\n");
            }

            sb.Append("}\n");
            return sb.ToString();
        }

        /// <summary>modId dizisini Ordinal sıralı JSON dizisine çevirir (boş = <c>[]</c>).</summary>
        private static string BuildModesArray(string[] modeIds)
        {
            if (modeIds == null || modeIds.Length == 0)
            {
                return "[]";
            }

            var ids = new List<string>(modeIds.Length);
            for (int i = 0; i < modeIds.Length; i++)
            {
                if (!string.IsNullOrWhiteSpace(modeIds[i]))
                {
                    ids.Add(modeIds[i]);
                }
            }

            if (ids.Count == 0)
            {
                return "[]";
            }

            ids.Sort(StringComparer.Ordinal);

            var sb = new StringBuilder("[");
            for (int i = 0; i < ids.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append(", ");
                }

                sb.Append('"').Append(EscapeJson(ids[i])).Append('"');
            }

            return sb.Append(']').ToString();
        }

        /// <summary>Minimal JSON string kaçışı (protokol anahtarları ASCII olsa da güvenli taraf).</summary>
        private static string EscapeJson(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            var sb = new StringBuilder(value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                switch (c)
                {
                    case '"':
                        sb.Append("\\\"");
                        break;
                    case '\\':
                        sb.Append("\\\\");
                        break;
                    case '\n':
                        sb.Append("\\n");
                        break;
                    case '\r':
                        sb.Append("\\r");
                        break;
                    case '\t':
                        sb.Append("\\t");
                        break;
                    default:
                        if (c < ' ')
                        {
                            sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            sb.Append(c);
                        }

                        break;
                }
            }

            return sb.ToString();
        }

        // ----------------------------------------------------------------- I/O

        /// <summary>UTF-8 (BOM'suz), LF satır sonlu yazım — içerik zaten <c>\n</c> ile kurulur.</summary>
        private static void WriteFile(string path, string content)
        {
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(path, content, new UTF8Encoding(false));
        }

        /// <summary>Dialog metni: özet + (varsa) uyarı listesi.</summary>
        private static string BuildDialogText(ServerConfigExportResult result)
        {
            var sb = new StringBuilder();
            sb.Append(result.Summary);

            if (result.Warnings.Count > 0)
            {
                sb.Append("\n\nUyarılar:");
                for (int i = 0; i < result.Warnings.Count; i++)
                {
                    sb.Append("\n• ").Append(result.Warnings[i]);
                }
            }

            return sb.ToString();
        }
    }
}
