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
    /// <c>Tools &gt; VortexArena &gt; Export Server Config</c> — projedeki
    /// <see cref="WeaponDefinition"/> ve <see cref="MapDefinition"/> ScriptableObject'lerinden
    /// sunucunun okuduğu <c>Server/config/weapons.json</c> + <c>Server/config/maps.json</c>
    /// dosyalarını üretir.
    /// <para>
    /// <b>Tek doğruluk kaynağı Unity SO'larıdır; sunucu hasarı bu tablodan uygular
    /// (Docs/ArenaNet-Protokol.md §10.3).</b> İstemcinin bildirdiği hasar tablodan saparsa
    /// sunucu kendi değerini uygular — bu yüzden export'u silah eklendikten/değiştirildikten
    /// sonra çalıştırmak ZORUNLUDUR (Faz 3'teki elle senkron burada otomatikleşir).
    /// </para>
    /// <para>
    /// <b>Determinizm (git diff'i temiz kalsın):</b> silahlar <c>weaponId</c>, haritalar
    /// <c>sceneName</c> ve mod listeleri Ordinal alfabetik sıralanır; satır sonu LF,
    /// kodlama UTF-8 BOM'suz, dosya sonunda tek <c>\n</c>. Aynı içerik → aynı bayt.
    /// </para>
    /// <para>
    /// <b>Güvenlik freni:</b> hiç SO bulunamazsa ilgili dosya YAZILMAZ (mevcut sunucu
    /// yapılandırması boş bir tabloyla ezilmesin) — bunun yerine uyarı döner.
    /// </para>
    /// </summary>
    public static class ServerConfigExporter
    {
        private const string WeaponsFileName = "weapons.json";
        private const string MapsFileName = "maps.json";

        /// <summary>Menü girişi — dialoglu (elle) export.</summary>
        [MenuItem("Tools/VortexArena/Export Server Config")]
        private static void ExportMenu()
        {
            Export(true);
        }

        /// <summary>
        /// weapons.json + maps.json üretir ve sonucu döner.
        /// <paramref name="showDialog"/> <c>false</c> iken HİÇBİR dialog açılmaz
        /// (MCP / batch otomasyonundan başlıksız çağrılabilir); özet her hâlükârda
        /// <see cref="Debug.Log"/> ile konsola yazılır.
        /// </summary>
        /// <param name="showDialog">Bitişte özet dialogu gösterilsin mi.</param>
        /// <returns>Yazılan yollar, satır sayıları ve doğrulama uyarıları.</returns>
        public static ServerConfigExportResult Export(bool showDialog)
        {
            var result = new ServerConfigExportResult();

            string configDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Server", "config"));
            result.WeaponsPath = Path.Combine(configDir, WeaponsFileName);
            result.MapsPath = Path.Combine(configDir, MapsFileName);

            List<WeaponDefinition> weapons = CollectWeapons(result);
            List<MapDefinition> maps = CollectMaps(result);

            result.WeaponCount = weapons.Count;
            result.MapCount = maps.Count;

            Directory.CreateDirectory(configDir);

            if (weapons.Count > 0)
            {
                WriteFile(result.WeaponsPath, BuildWeaponsJson(weapons));
            }
            else
            {
                result.Warnings.Add($"Hiç WeaponDefinition bulunamadı — {WeaponsFileName} YAZILMADI (mevcut sunucu tablosu korundu).");
            }

            if (maps.Count > 0)
            {
                WriteFile(result.MapsPath, BuildMapsJson(maps));
            }
            else
            {
                result.Warnings.Add($"Hiç MapDefinition bulunamadı — {MapsFileName} YAZILMADI (mevcut sunucu tablosu korundu).");
            }

            result.Summary =
                $"Export Server Config: {result.WeaponCount} silah + {result.MapCount} harita → {configDir} ({result.Warnings.Count} uyarı)";

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

        // -------------------------------------------------------------- toplama

        /// <summary>Tüm projedeki silah tanımlarını toplar, doğrular ve weaponId'ye göre sıralar.</summary>
        private static List<WeaponDefinition> CollectWeapons(ServerConfigExportResult result)
        {
            var loaded = new List<WeaponDefinition>();
            var paths = new Dictionary<WeaponDefinition, string>();

            string[] guids = AssetDatabase.FindAssets("t:WeaponDefinition");
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                var asset = AssetDatabase.LoadAssetAtPath<WeaponDefinition>(path);
                if (asset == null)
                {
                    continue;
                }

                loaded.Add(asset);
                paths[asset] = path;
            }

            loaded.Sort((a, b) =>
            {
                int byId = string.CompareOrdinal(a.WeaponId ?? string.Empty, b.WeaponId ?? string.Empty);
                return byId != 0 ? byId : string.CompareOrdinal(paths[a], paths[b]);
            });

            var accepted = new List<WeaponDefinition>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < loaded.Count; i++)
            {
                WeaponDefinition weapon = loaded[i];
                string path = paths[weapon];
                string id = weapon.WeaponId;

                if (string.IsNullOrWhiteSpace(id))
                {
                    result.Warnings.Add($"Boş weaponId: '{path}' — atlandı (protokol anahtarı zorunlu).");
                    continue;
                }

                if (!seen.Add(id))
                {
                    result.Warnings.Add($"Yinelenen weaponId '{id}': '{path}' — atlandı (ilk eşleşme yazıldı).");
                    continue;
                }

                if (weapon.Damage <= 0f)
                {
                    result.Warnings.Add($"weaponId '{id}' damage <= 0 ({weapon.Damage}) — '{path}'.");
                }

                if (weapon.FireRateRpm <= 0f)
                {
                    result.Warnings.Add($"weaponId '{id}' rpm <= 0 ({weapon.FireRateRpm}) — '{path}'.");
                }

                accepted.Add(weapon);
            }

            return accepted;
        }

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

                if (map.SpawnSlotsPerTeam <= 0)
                {
                    result.Warnings.Add($"sceneName '{sceneName}' spawnSlotsPerTeam <= 0 ({map.SpawnSlotsPerTeam}) — '{path}'.");
                }

                accepted.Add(map);
            }

            return accepted;
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
        /// <c>{ "weapons": [ { "weaponId": "ak47", "damage": 34, "rpm": 600 } ] }</c> —
        /// mevcut elle yazılmış dosyanın biçimi birebir korunur (2 boşluk girinti, satır başına
        /// bir silah, aynı alan sırası).
        /// </summary>
        private static string BuildWeaponsJson(List<WeaponDefinition> weapons)
        {
            var sb = new StringBuilder();
            sb.Append("{\n");

            if (weapons.Count == 0)
            {
                sb.Append("  \"weapons\": []\n");
            }
            else
            {
                sb.Append("  \"weapons\": [\n");
                for (int i = 0; i < weapons.Count; i++)
                {
                    WeaponDefinition weapon = weapons[i];
                    sb.Append("    { \"weaponId\": \"").Append(EscapeJson(weapon.WeaponId))
                        .Append("\", \"damage\": ").Append(Number(weapon.Damage))
                        .Append(", \"rpm\": ").Append(Mathf.RoundToInt(weapon.FireRateRpm).ToString(CultureInfo.InvariantCulture))
                        .Append(" }")
                        .Append(i < weapons.Count - 1 ? ",\n" : "\n");
                }

                sb.Append("  ]\n");
            }

            sb.Append("}\n");
            return sb.ToString();
        }

        /// <summary>
        /// <c>{ "maps": [ { "sceneName": "Arena10x10", "sizeX": 10, "sizeZ": 10,
        /// "spawnSlotsPerTeam": 4, "modes": ["tdm"] } ] }</c> — sunucunun start_match
        /// doğrulaması ve ileriki bölge tabanlı modlar için.
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
                        .Append("\", \"sizeX\": ").Append(Number(map.Size.x))
                        .Append(", \"sizeZ\": ").Append(Number(map.Size.y))
                        .Append(", \"spawnSlotsPerTeam\": ").Append(map.SpawnSlotsPerTeam.ToString(CultureInfo.InvariantCulture))
                        .Append(", \"modes\": ").Append(BuildModesArray(map.SupportedModeIds))
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

        /// <summary>Ondalık sayıyı kültürden bağımsız, gereksiz ondalık basamak olmadan yazar.</summary>
        private static string Number(float value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
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
